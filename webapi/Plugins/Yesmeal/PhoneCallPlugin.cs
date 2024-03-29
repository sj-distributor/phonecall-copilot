using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CopilotChat.WebApi.Dtos;
using CopilotChat.WebApi.Models.Request;
using CopilotChat.WebApi.Models.Response;
using CopilotChat.WebApi.Options;
using CopilotChat.WebApi.Plugins.Utils;
using CopilotChat.WebApi.Storage;
using DocumentFormat.OpenXml.Office.CustomUI;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;

namespace CopilotChat.WebApi.Plugins.Yesmeal;

public class PhoneCallPlugin
{
    private IHttpClientFactory _httpClientFactory;
    private ThirdPartyTokenOptions _thirdPartyTokenOptions;
    private static Dictionary<string, MerchFoodDto> merchFoodDic = new Dictionary<string, MerchFoodDto>();

    public PhoneCallPlugin(IHttpClientFactory httpClientFactory,
        IOptions<ThirdPartyTokenOptions> thirdPartyTokenOptions)
    {
        _httpClientFactory = httpClientFactory;
        _thirdPartyTokenOptions = thirdPartyTokenOptions.Value;
    }

    [KernelFunction, Description("spot the intent")]
    public   async Task<string> IntentSpotAsync(KernelArguments arguments)
    {
        if (arguments["question"] == null) return "抱歉，系统暂时开了小差，请重新发送消息～";
        var chatHistory = arguments["ChatHistory"].ToString();
        var question = arguments["question"].ToString();

        if (merchFoodDic.Values.Any(x => x.ParameterGroups.Exists(t => !t.IsAnswer)))
        {
            Console.WriteLine("has SpecificationsFoods need to select");
            return await AddSpecificationsFoodsync(question);
        }
        Console.WriteLine("normal");
        var statementOrCommandIntentResult = await AskGptAsync(GetStatementOrCommandIntent(question));
        if (statementOrCommandIntentResult.Data.Response == null)
            return  await Task.FromResult("抱歉，无法识别您的意图，请再换一种方式提问好吗？");

        var askIntentDto = JsonConvert.DeserializeObject<AskIntentDto>(statementOrCommandIntentResult.Data.Response.Replace("\n","").Replace(" ",""));
        if (!string.IsNullOrWhiteSpace(askIntentDto.AnswerPhrase))
        {
            var askPhraseIntentResult = await AskGptAsync(GetPhraseIntentMap(askIntentDto.AnswerPhrase, chatHistory));
            var phraseIntentValue = JsonConvert.DeserializeObject<PhraseIntentValueDto>(askPhraseIntentResult.Data.Response);
            if (!phraseIntentValue.IsPositive)
            {

                return "好的，请问还有什么可以帮到你吗？";
            }
            var result= await CallIntentFunction(false, phraseIntentValue.IntentValue);
            return PolishIntentAnswer(phraseIntentValue.IntentValue, result);
        }

        var askIntentMapResult = await AskGptAsync(GetStandardIntentMap(string.IsNullOrWhiteSpace(askIntentDto.ActionContent) ? question:askIntentDto.ActionContent, chatHistory));
        if (askIntentMapResult.Data.Response == null)
            return await Task.FromResult("抱歉，无法识别您的意图，请再换一种方式提问好吗？");
        var intentValue = JsonConvert.DeserializeObject<IntentValueDto>(askIntentMapResult.Data.Response);

        return await CallIntentFunction(askIntentDto.IsAsking, intentValue);
    }

    private List<IntentValueDto> GetStandardIntentInfo()
    {
        return new List<IntentValueDto>
        {
            new() { Intent = "地址", Value = 0 },
            new() { Intent = "活动", Value = 1 },
            new() { Intent = "停车场", Value = 2 },
            new() { Intent = "推荐菜", Value = 3 },
            new() { Intent = "下单", Value = 4 },
            new() { Intent = "加入购物车", Value = 5, FoodName = "" },
            new() { Intent = "删除商品", Value = 6, FoodName = "" },
            new() { Intent = "订单详情", Value = 7 }
        };
    }

    private List<IntentValueDto> GetContextAnswerPhrase()
    {
        return new List<IntentValueDto>
        {
            new() { Intent = "同意", Value = 0 },
            new() { Intent = "确认", Value = 1 },
            new() { Intent = "好的", Value = 2 },
            new() { Intent = "不要了", Value = 3 },
            new() { Intent = "获取订单详情", Value = 7 }
        };
    }

    private  string PolishIntentAnswer(IntentValueDto intentValue,  string answer)
    {
        switch (intentValue.Value)
        {
            case 0:
                return $"你好，地址是：{answer}";
            case 1:
                return $"活动内容如下：\n {answer} \n 请问还有什么可以帮到你吗？";
            case 2:
                return "你好，暂时没停车场哦，请问还有什么可以帮到你？";
            case 3:
                return answer;
            case 4:
                return answer;
            case 5:
                return answer;
            case 6:
                return "暂不支持的操作";
            case 7:
                return answer;
            default:
                return "识别不到的意图";
        }
    }

    private async Task<string> CallIntentFunction(bool isAsking, IntentValueDto intentValue)
    {
        switch (intentValue.Value)
        {
            case 0:
                return await GetMerchantAddress(isAsking);
            case 1:
                return await GetMerchantCampaign(isAsking);
            case 2:
                return await GetMerchantParkingInfo(isAsking);
            case 3:
                return await GetRecommendDishWithoutSpecificCategoryName(isAsking);
            case 4:
                return await AddOrderByMerchIdAsync(isAsking);
            case 5:
                return await AskForFoodDetail(intentValue.FoodName, "1", "", isAsking);
            case 6:
                return "暂不支持的操作";
            case 7:
                return await GetMerchantOrderDetailAsync();
            default:
                return "识别不到的意图";
        }
    }

    [KernelFunction, Description("Get campaign/activities of merchant，restaurant,")]
    public async Task<string> GetMerchantCampaign(bool isAsking)
    {
        if (isAsking)
            return await Task.FromResult("最近有活动，需要我帮你查询吗？");

        return await AsyncUtils.SafeInvokeAsync<string>(async () =>
        {
            using var  httpClient = CreateYesmealHttpClient();

            var response = await httpClient.GetAsync("https://testapi.yamimeal.com/api/MerchCouponDistributionCampaign/query?CampaignType=1").ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<GetMerchCouponDistributionCampaignsByQueryResponse>();
            var originalAnswer = result?.Campaigns.FirstOrDefault()?.MerchCouponPromotions.FirstOrDefault()?.Description;
            Console.WriteLine(originalAnswer);
            if (!string.IsNullOrWhiteSpace(originalAnswer))
                originalAnswer = await Translation(originalAnswer);

            var answer = originalAnswer ??
                         "感谢您的关注。我们目前正在考虑不同的优惠活动，以回馈我们的顾客。请您持续关注我们的店铺，我们会在未来很快推出一些特别的优惠，让您获得更多的实惠和惊喜！";

            return await Task.FromResult(answer);
        }, nameof(GetMerchantCampaign));
    }

    [KernelFunction, Description("Get the address or location of a business or restaurant")]
    public   async Task<string> GetMerchantAddress(bool isAsking)
    {
        return await AsyncUtils.SafeInvokeAsync<string>(async () =>
        {
            using var  httpClient = CreateYesmealHttpClient();

            var response = await httpClient
                .GetAsync("https://testapi.yamimeal.com/api/Merch/get?Id=3bd51ea0-9b3e-45f2-92b7-c30fb162f910")
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
            var merch = await response.Content.ReadFromJsonAsync<GetMerchByIdRespone>();

            Console.WriteLine(merch?.Result.Address);
            return await Task.FromResult(merch?.Result.Address ?? string.Empty);
        }, nameof(GetMerchantAddress));
    }

    [KernelFunction, Description("Get merchant parking information")]
    public   async Task<string> GetMerchantParkingInfo(bool isAsking)
    {
        return await Task.FromResult("暂无停车场");
    }

    [KernelFunction, Description("Recommend a dish without specific category names，for example : user asked if he could recommend a dish")]
    public   async Task<string> GetRecommendDishWithoutSpecificCategoryName(bool isAsking)
    {
        Console.WriteLine("hit GetRecommendDishWithoutSpecificCategoryName");
        var merchId = Guid.Parse("3bd51ea0-9b3e-45f2-92b7-c30fb162f910");
        var FoodCategory = new[] { "牛肉", "豬肉", "雞肉", "麵類", "粥" };
        var random = new Random();
        int randomNumber = random.Next(1, 6); // 生成1到5之间的随机数
        var recommendFood = await GetRecommendFoodAsync(merchId, foodName: FoodCategory[randomNumber - 1]);
        if (recommendFood == null)
            return await Task.FromResult("今天暂无推荐菜哦。请问还有什么可以帮到你？");

        var resultTemplate = $"今日推荐：{recommendFood.Name}, 价钱：{recommendFood.Price}, 需要帮你加入购物车吗？";
        return await Task.FromResult(resultTemplate);
    }

    [KernelFunction, Description("Recommend a dish with specific category names，for example : user asked if there were any beef dishes to recommend.")]
    public   async Task<string> GetRecommendDishWithSpecificCategoryName([Description("specific category name")]string categoryName,
        KernelArguments args)
    {
        Console.WriteLine("hit GetRecommendDishWithSpecificCategoryName");
        var merchId = Guid.Parse("3bd51ea0-9b3e-45f2-92b7-c30fb162f910");
        var recommendFood = await GetRecommendFoodAsync(merchId, foodName: categoryName);
        if (recommendFood == null)
            return await Task.FromResult($"今天暂无{categoryName}的推荐菜哦。请问还有什么可以帮到你？");

        var resultTemplate = $"今日推荐：{recommendFood.Name}, 价钱：{recommendFood.Price}, 需要帮你加入购物车吗？";
        return await Task.FromResult(resultTemplate);
    }

    [KernelFunction, Description("customer want to eat or order specific dish name, for example, milk, tea, rice, sandwich, chicken chop, pork chop, lunch meat, egg, fish, ham, noodles, porridge, vegetable")]
    public   async Task<string> AskForFoodDetail(
        [Description("the name of food")]string foodName,
        [Description("the quantity of food")]string quantity,
        [Description("the special comment of food")]string specialComment,
        bool isAsking)
    {
        Console.WriteLine("hit the AskForFoodDetail:" + foodName);

        return await AsyncUtils.SafeInvokeAsync<string>(async () =>
        {
            var merchId = Guid.Parse("3bd51ea0-9b3e-45f2-92b7-c30fb162f910");
            var recommendFood = await GetRecommendFoodAsync(merchId, foodName);
            if (recommendFood == null) return $"暂无{foodName},请换一个好吗？";

            if (recommendFood.ParameterGroups.Count == 0)
            {
                if (isAsking)
                    return await Task.FromResult($"查询到{recommendFood.Name},价钱：{recommendFood.Price}, 需要帮你加入购物车吗？");
                var resultTemplate = $"你好，{recommendFood.Name}已帮你加入购物车。需要埋单吗？";

                await AddToCartAsync(merchId, recommendFood, 1);
                return await Task.FromResult(resultTemplate);
            }
            else
            {
                var stringBuilder = new StringBuilder($"查询到{recommendFood.Name},价钱：{recommendFood.Price}");
                var parameterGroup = recommendFood.ParameterGroups.First();
                stringBuilder.Append($"\n ，在规格 {parameterGroup.Name}({parameterGroup.Description})，分别有：");
                foreach (var item in parameterGroup.ParameterItems)
                {
                    stringBuilder.Append($"[{item.Name}，价格：{item.Price}] ,");
                }

                merchFoodDic[recommendFood.Id.ToString()] = recommendFood;
                stringBuilder.Append("\n 请问你需要哪一个？");
                return await Task.FromResult(stringBuilder.ToString());
            }
        }, nameof(AskForFoodDetail));
    }

    [KernelFunction, Description("customer want to check order detail")]
    public  async Task<string> GetMerchantOrderDetailAsync()
    {
        var merchId = Guid.Parse("3bd51ea0-9b3e-45f2-92b7-c30fb162f910");
        using var  httpClient = CreateYesmealHttpClient();

        var response = await httpClient.GetAsync($"https://testapi.yesmeal.com/api/shoppingcart/bymerch?merchid={merchId}")
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var orderDetailForMerch = await response.Content.ReadFromJsonAsync<GetCurrentUserShoppingCartByMerchIdResponse>();
        if (orderDetailForMerch?.ShoppingCart == null || !orderDetailForMerch.ShoppingCart.ShoppingCartItems.Any())
            return "你的购物车暂时没商品，请先进行下单，谢谢";
        var result = new StringBuilder();
        result.Append("您的订单详情如下：\n\n");
        for (var i = 0; i < orderDetailForMerch.ShoppingCart.ShoppingCartItems.Count; i++)
        {
            var item = orderDetailForMerch.ShoppingCart.ShoppingCartItems[i];
            var parameterFoodDesc = item.ShoppingCartItemParams.Any() ? string.Join(",",item.ShoppingCartItemParams.Select(t => t.Name)) : " ";

            result.Append($"{i + 1}.{item.FoodName} {parameterFoodDesc}---单价：{item.Price} ---数量:{item.Quantity}；\n\n");
        }
        result.Append($"\n 总金额：{orderDetailForMerch.ShoppingCart.CartTotal}");
        return result.ToString();
    }

    [KernelFunction, Description("customer want to place an order")]
    public  async Task<string> AddOrderByMerchIdAsync(bool isAsking)
    {
        var merchId = Guid.Parse("3bd51ea0-9b3e-45f2-92b7-c30fb162f910");
        using var  httpClient = CreateYesmealHttpClient();

        var httpContent = new StringContent(JsonConvert.SerializeObject(new
            AddOrderByMerchIdRequest
            {
                MerchId = merchId
            }));
        httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var orderDetailResult = await GetMerchantOrderDetailAsync();
        var response = await httpClient.PostAsync("https://testapi.yesmeal.com/api/order/by/phonecall",httpContent)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AddOrderByMerchIdResponse>();

        return $"{orderDetailResult} \n\n 下单成功，你的取餐号为：{result.MealCode}，请在{result.PickupTime}左右到店pick up，多谢。";
    }

    [KernelFunction, Description("customers have selected the respective product/food specifications.")]
    [return: Description("Please be careful not to alter the returned original text information")]
    public   async Task<string> AddSpecificationsFoodsync([Description("Food items with specified specifications")]string specificationsName)
    {
        var askGptRequest = new AskGptRequest
        {
            Model = 6,
            ResponseFormat = new ResponseFormat { Type = "json_object" },
            Messages = new List<AskSmartiesMessageDto>
            {
                new()
                {
                    Role = "user",
                    Content = "根据用户给的商品规格名称和商品JSON能匹配出对应的id和parameterItemId；" +
                              $"商品JSON:{JsonConvert.SerializeObject(merchFoodDic)};" +
                              "如果找到匹配项，输出的格式一定要是符合这个JSON：[{\"foodId\": \"xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx\", \"parameterItemId\":\"xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx\"}];" +
                              "如果没有找到匹配项，则只需要返回[]，一定不要加入商品JSON不存在的id，确保程序能够正确地解析用户输入的规格名称，并从提供的 JSON 数据中查找匹配项;",
                },
                new()
                {
                    Role = "system",
                    Content = "商品规格名称:" + specificationsName
                }
            }
        };
        var askGptResult = await AskGptAsync(askGptRequest);
        var foodParameterMap = JsonConvert.DeserializeObject<FoodParameterMapDto>(askGptResult.Data.Response);
        if (foodParameterMap == null && foodParameterMap.FoodId != Guid.Empty)
            throw new Exception("Response mapping異常:" + askGptResult.Data.Response);

        var foodObj = merchFoodDic[foodParameterMap.FoodId.ToString()];
        if (foodObj != null)
        {
            var foodParameter = merchFoodDic[foodParameterMap.FoodId.ToString()];
            var foodItemParameter = foodParameter.ParameterGroups
                .SelectMany(x => x.ParameterItems).FirstOrDefault(x => x.Id == foodParameterMap.ParameterItemId);
            var foodGroupParameter = foodParameter.ParameterGroups.FirstOrDefault(x => x.Id == foodItemParameter.GroupId);
            foodGroupParameter.IsAnswer = foodItemParameter.IsSelected = true;
            if (foodParameter.ParameterGroups.All(x => x.IsAnswer))
            {
                var merchId = Guid.Parse("3bd51ea0-9b3e-45f2-92b7-c30fb162f910");
                var selectedFoodParamList = foodParameter.ParameterGroups.Where(x => x.IsAnswer)
                    .Select(x => x.ParameterItems.First(t => t.IsSelected)).Select(x => new FoodParameterDto
                    {
                        ParameterId = x.Id, Quantity = 1, ParameterGroupId = x.GroupId
                    }).ToList();
                await AddToCartAsync(merchId, foodObj, 1, selectedFoodParamList);
            }
            else
            {
                var needSelectedFoodGroup = foodParameter.ParameterGroups.FirstOrDefault(x => !x.IsAnswer);
                var stringBuilder = new StringBuilder();

                stringBuilder.Append(
                    $"好的，已帮你选择{foodGroupParameter.Name}规格：{foodItemParameter.Name} \n 在{needSelectedFoodGroup.Name}规格方面还有以下需要选择：");
                foreach (var item in needSelectedFoodGroup.ParameterItems)
                {
                    stringBuilder.Append($"[{item.Name}，价格：{item.Price}] ,");
                }

                stringBuilder.Append("\n 请问你需要哪一个？");
                return await Task.FromResult(stringBuilder.ToString());
            }

            merchFoodDic = new Dictionary<string, MerchFoodDto>();
            return await Task.FromResult("好的，已帮你加入购物车。请问还需要其他吗？还是埋单吗？");
        }

        return await Task.FromResult("抱歉，系统开了小差，请再说多一次好吗？");
    }

    private   async Task<MerchFoodDto> GetRecommendFoodAsync(Guid merchId, string foodName = null)
    {
        return await AsyncUtils.SafeInvokeAsync<MerchFoodDto>(async () =>
        {
            using var  httpClient = CreateYesmealHttpClient(new Dictionary<string, string>{ {"LanguageCode", "zh-TW"}});
            var httpContent = new StringContent(JsonConvert.SerializeObject(new RecommendSimilarFoodsRequest
            {
                Keyword = foodName, MerchIds = new List<Guid> { merchId },
                RecommendCount = 1
            }));
            httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            var response = await httpClient.PostAsync("https://testapi.yamimeal.com/api/Ai/food/similar", httpContent).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
            Console.WriteLine(await response.Content.ReadAsStringAsync());
            var recommendSimilarFoods = await response.Content.ReadFromJsonAsync<RecommendSimilarFoodsResponse>();
            var recommendFood = recommendSimilarFoods?.SimilarFoods.FirstOrDefault();

            return await Task.FromResult(recommendFood);
        }, nameof(GetRecommendFoodAsync));
    }

    private   async Task<string> Translation(string content)
    {
        using var  httpClient = CreateSmartiesHttpClient();

        var httpContent = new StringContent(JsonConvert.SerializeObject(new
            TranslationRequest
            {
                TranslateFrom = 0, TargetLanguage = 0,
                Content = content
            }));

        var response = await httpClient.PostAsync("https://smarties.yamimeal.ca/api/Translation",httpContent)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private   async Task<AskGptResponse> AskGptAsync(AskGptRequest request)
    {
        return await AsyncUtils.SafeInvokeAsync<AskGptResponse>(async () =>
        {
            using var httpClient = CreateSmartiesHttpClient();
            var httpContent = new StringContent(JsonConvert.SerializeObject(request));
            httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            var response = await httpClient.PostAsync("https://testsmarties.yamimeal.ca/api/Ask/general/query", httpContent)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var askGptResult = await response.Content.ReadFromJsonAsync<AskGptResponse>();
            return askGptResult;
        },nameof(AskGptAsync));
    }

    private   async Task<string> AddToCartAsync(Guid merchId, MerchFoodDto merchFood,int quantity,
        List<FoodParameterDto>? foodParameters = null)
    {
        using var httpClient = CreateYesmealHttpClient();

        var httpContent = new StringContent(JsonConvert.SerializeObject(new
            AddOrUpdateItemToCartRequest
            {
                MerchId = merchId,
                FoodId = merchFood.Id,
                Quantity = quantity,
                FoodParameters = foodParameters ?? new List<FoodParameterDto>(),
                DeliveryType = 0,
                ShouldThrowGroupifyError = false,
                ShouldExcludePickupOrFallbackMerchants = false,
            }));
        httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await httpClient.PostAsync("https://testapi.yesmeal.com/api/shoppingcart/additem",httpContent)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    [KernelFunction, Description("empty/clear/remove the shoppingcart")]
    public   async Task EmptyCartAsync()
    {
        using var  httpClient = CreateYesmealHttpClient();

        var httpContent = new StringContent(JsonConvert.SerializeObject(new
            EmptyShoppingCartRequest
            {
                MerchId = Guid.Parse("3bd51ea0-9b3e-45f2-92b7-c30fb162f910"),
                ShouldThrowGroupifyError = false,
                ShouldExcludePickupOrFallbackMerchants = false
            }));
        httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var response = await httpClient.PostAsync("https://testapi.yesmeal.com/api/shoppingcart/empty",httpContent)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
    }

    private   AskGptRequest GetStatementOrCommandIntent(string question)
    {
        return new AskGptRequest
        {
            Model = 6,
            ResponseFormat = new ResponseFormat { Type = "json_object" },
            Messages = new List<AskSmartiesMessageDto>
            {
                new()
                {
                    Role = "system",
                    Content = "你是一位对中文理解深入的人工智能。请根据用户的提问来判断，它属于问题咨询还是命令操作。" +
                              "如果是问题咨询，通常问题咨询会包含疑问词但不明确要求执行动作的情况，那么这个不明确要求执行动作也要填充ActionContent。" +
                              "对于没有明显动作，属于咨询“活动”，“推荐菜”，“停车场”，“地址”的提问，也请把这个名词填充到ActionContent。" +
                              "对于简短的动词提问，如‘同意’，‘需要’，‘确认’，‘好’，‘不要’，‘拒绝’，请将提问内容填充到 AnswerPhrase。" +
                              "对于语句中含有“吗”，“是不是”的词语，都属于问题咨询，IsAsking都要设置true。" +
                              "如果提问明确要获取一些信息，但是没直接使用疑问词，他就属于命令操作，IsAsking都要设置false。" +
                              "一些动词开头的提问，属于命令操作，比如“查询”，“下单”，“落单”。切记下单不可能和菜品名称同时存在，因此你不要加入与餐饮无关的内容。" +
                              "输出的格式必须符合以下 JSON 结构：{\"IsAsking\": \"\", \"ActionContent\": \"\", \"AnswerPhrase\": \"\"}。"
                },
                new()
                {
                    Role = "user",
                    Content = "输入:" + question
                }
            }
        };
    }

    private AskGptRequest GetStandardIntentMap(string input, string context=null)
    {
        return new AskGptRequest
        {
            Model = 6,
            ResponseFormat = new ResponseFormat { Type = "json_object" },
            Messages = new List<AskSmartiesMessageDto>
            {
                new()
                {
                    Role = "system",
                    Content = "你是一个对餐饮有高度理解力的人工智能,我希望根据输入的内容和下面这个JSON对象做一个匹配；" +
                              "如果输入内容匹配到加入购物车或者删除购物车商品，请把输入的内容进行拆分，拆出商品/食品名称填充到foodName；" +
                              "你的输出的格式也一定是这个JSON里面的某个对象，只能输出你匹配度最高的一个，如果没有任何一个intent可以匹配，那么返回的intent里面设置''；同时请你结合上下文来分析；" +
                              $"匹配JSON：{JsonConvert.SerializeObject(GetStandardIntentInfo())};"
                },
                new()
                {
                    Role = "user",
                    Content = $"输入：{input};上下文内容：{context}"
                }
            }
        };
    }

    private AskGptRequest GetPhraseIntentMap(string input, string context)
    {
        return new AskGptRequest
        {
            Model = 6,
            ResponseFormat = new ResponseFormat { Type = "json_object" },
            Messages = new List<AskSmartiesMessageDto>
            {
                new()
                {
                    Role = "system",
                    Content = "你是一个对餐厅下单有高度理解力的人工智能,我希望你能够根据用户输入的内容和上下文内容来推断出顾客想要做的动作，" +
                              "顾客做的动作仅限以下动作JSON里面的intent，请你抽取所有intent做一个匹配," +
                              "你返回的的格式必须符合这个JSON 结构：{\"IsPositive\":\"\",\"IntentValue\":{\"intent\": \"\", \"value\": \"\", \"FoodName\":\"\"}}。" +
                              "当你抽取了Intent，对应的Value也能匹配到，如果返回的对象里面Value为''，Intent也是一定为''。" +
                              "如果用户的输入是属于肯定词语，则IsPositive设置为true，否则为false，IntentValue里面的内容和动作JSON里面的对象匹配；" +
                              $"动作JSON：{JsonConvert.SerializeObject(GetStandardIntentInfo())}"
                },
                new()
                {
                    Role = "user",
                    Content = $"输入:{input}；上下文内容:{context}"
                }
            }
        };
    }

    private   HttpClient CreateYesmealHttpClient(Dictionary<string, string>? headers = null)
    {
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_thirdPartyTokenOptions.Yesmeal}");
        httpClient.DefaultRequestHeaders.Add("Source_System",
            headers == null || !headers.ContainsKey("SourceSystem") ? "1" : headers["SourceSystem"]);
        httpClient.DefaultRequestHeaders.Add("language_code",
            headers == null || !headers.ContainsKey("LanguageCode") ? "zh-TW" : headers["LanguageCode"]);
        return httpClient;
    }

    private   HttpClient CreateSmartiesHttpClient()
    {
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_thirdPartyTokenOptions.Smarties}");
        httpClient.DefaultRequestHeaders.Add("accept",  "text/plain");
        return httpClient;
    }
}
