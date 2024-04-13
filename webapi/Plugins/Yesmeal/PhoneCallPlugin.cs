using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using CopilotChat.WebApi.Dtos;
using CopilotChat.WebApi.Models.Request;
using CopilotChat.WebApi.Models.Response;
using CopilotChat.WebApi.Options;
using CopilotChat.WebApi.Plugins.Utils;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;

namespace CopilotChat.WebApi.Plugins.Yesmeal;

public class PhoneCallPlugin
{
    private IHttpClientFactory _httpClientFactory;
    private ThirdPartyTokenOptions _thirdPartyTokenOptions;
    private static Dictionary<string, MerchFoodDto> specificationFoodDic = new Dictionary<string, MerchFoodDto>();
    private static Dictionary<string, MerchFoodDto> askFoodHistoryDic = new Dictionary<string, MerchFoodDto>();
    private static List<string> specialComments = new List<string>();
    public PhoneCallPlugin(IHttpClientFactory httpClientFactory,
        IOptions<ThirdPartyTokenOptions> thirdPartyTokenOptions)
    {
        _httpClientFactory = httpClientFactory;
        _thirdPartyTokenOptions = thirdPartyTokenOptions.Value;
    }

    [KernelFunction, Description("spot the intent")]
    public async Task<string> IntentSpotAsync(KernelArguments arguments)
    {
        if (arguments["question"] == null) return "抱歉，系统暂时开了小差，请重新发送消息～";
        var latestChatHistory = arguments["ChatHistory"].ToString();
        if (latestChatHistory.Length > 150)
            latestChatHistory = latestChatHistory.Substring(0, 150) + "...";

        var question = arguments["question"].ToString();

        if (specificationFoodDic.Values.Any(x => x.ParameterGroups.Exists(t => !t.IsAnswer)))
        {
            Console.WriteLine("has SpecificationsFoods need to select");
            return await AddSpecificationsFoodsync(question, latestChatHistory);
        }

        var questionIntentResponse = await AskGptAsync(GetQuestionIntentRequest(question, latestChatHistory));
        return await PolishQuestionIntentAsync(question, questionIntentResponse.Data.Response, latestChatHistory);
    }

    private async Task<string> PolishQuestionIntentAsync(string question, string questionIntent, string chatHistory,
        IntentScenes? intentScenes = null)
    {
        var intentValue = questionIntent.Split(':')[1]?.Trim();
        var resultTmp = string.Empty;
        switch (intentValue)
        {
            case "NONE":
                if (intentScenes == IntentScenes.Specification)
                    specificationFoodDic.Clear();

                var questionIntentResponse = await AskGptAsync(GetFoodAssistantAnswerRequest(question, chatHistory));
                return questionIntentResponse.Data.Response;
            case "AskForAddress":
                resultTmp = $"你好，地址是：{await GetMerchantAddress(false)}";
                break;
            case "GetActivity":
                resultTmp = $"活动内容如下：\n {await GetMerchantCampaign(false)} \n 请问还有什么可以帮到你吗？";
                break;
            case "CheckParkingLotExists":
                resultTmp = $"你好：\n {await GetMerchantParkingInfo(false)} \n 请问还有什么可以帮到你吗？";
                break;
            case "IntroducingRecommendedDishes":
                resultTmp = await GetRecommendDishWithoutSpecificCategoryName(false);
                break;
            case "AddOrder":
                resultTmp = await AddOrderByMerchIdAsync(false);
                break;
            case "AskFoodDetail":
                var askFoodDetailResponse = await AskGptAsync(GetFoodDetailRequest(question, chatHistory));
                var foodSpotDtoForFoodDetails = JsonConvert.DeserializeObject<List<FoodSpotDto>>(askFoodDetailResponse.Data.Response);

                var foodSpotDtoForFoodDetail = foodSpotDtoForFoodDetails.FirstOrDefault();
                resultTmp = await AskForFoodDetail(foodSpotDtoForFoodDetail.FoodName, foodSpotDtoForFoodDetail.Quantity.GetValueOrDefault().ToString(),
                    foodSpotDtoForFoodDetail.SpecialRequirement, true);
                break;
            case "AddCart":
                var askAddCartResponse = await AskGptAsync(GetFoodDetailRequest(question, chatHistory));
                var foodSpotDtoForCarts = JsonConvert.DeserializeObject<List<FoodSpotDto>>(askAddCartResponse.Data.Response);

                var foodSpotDtoForCart = foodSpotDtoForCarts.FirstOrDefault();
                if (foodSpotDtoForCart.FoodName != null)
                {
                    resultTmp = await AskForFoodDetail(foodSpotDtoForCart.FoodName,
                        foodSpotDtoForCart.Quantity.GetValueOrDefault().ToString(),
                        foodSpotDtoForCart.SpecialRequirement, false, IntentSource.AskAddCart, intentScenes,
                        chatHistory);
                }
                else
                {
                    resultTmp = "你好，不太清楚你要的菜品，可以再和我说多一次吗？";
                }
                break;
            case "OrderDetail":
                resultTmp = await GetMerchantOrderDetailAsync();
                break;
        }

        return resultTmp;
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
        var randomNumber = random.Next(1, 6); // 生成1到5之间的随机数
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

     public async Task<string> AskForFoodDetail(
        [Description("the name of food")]string foodName,
        [Description("the quantity of food")]string quantity,
        [Description("the special comment of food")]string specialComment,
        bool isAsking, IntentSource? intentSource = null,
        IntentScenes? intentScenes = null, string chatHistory = null)
    {
        Console.WriteLine("hit the AskForFoodDetail:" + foodName);

        return await AsyncUtils.SafeInvokeAsync<string>(async () =>
        {
            var merchId = Guid.Parse("3bd51ea0-9b3e-45f2-92b7-c30fb162f910");

            if (intentSource == IntentSource.AskAddCart && intentScenes == IntentScenes.Specification)
            {
                var food = askFoodHistoryDic.Values.FirstOrDefault(x => x.Name.Contains(foodName));

                if (food?.ParameterGroups != null && food.ParameterGroups.Any())
                {
                    var stringBuilder = new StringBuilder($"你好，我还不知道你要什么规格的{food.Name}，{food.Name} 有：");
                    var parameterGroup = food.ParameterGroups.First();
                    foreach (var item in parameterGroup.ParameterItems)
                    {
                        stringBuilder.Append($"[{item.Name}]，");
                    }

                    stringBuilder.Append("\n 请问你需要哪一个规格？");

                    return await Task.FromResult(stringBuilder.ToString());
                }
                else
                {
                    var resultTemplate = $"好的，{food.Name}已帮你加入购物车。请问还需要其他帮助吗？";

                    await AddToCartAsync(merchId, food, 1);
                    return await Task.FromResult(resultTemplate);
                }
            }

            var recommendFood = await GetRecommendFoodAsync(merchId, foodName);
            if (recommendFood == null) return $"暂无{foodName},请换一个好吗？";

            if (!string.IsNullOrWhiteSpace(specialComment))
            {
                //如果当前商品是没规格的，而specialComment又是含有规格的意思，那么就要提示用户，当前商品是单规格商品，作为specialComment传递即可
                //如果当前商品是多规格的，而specialComment又是含有规格的意思，那么先要hit中是哪个规格，设置 foodGroupParameter.IsAnswer = foodItemParameter.IsSelected = true;
                var askFoodIntent = await AskGptAsync(this.GetFoodSpecificationOrSpecialCommentRequest(specialComment, chatHistory));
                var intentValue = askFoodIntent.Data.Response.Split(':')[1].Trim();
                if (intentValue == "Specification" && recommendFood.ParameterGroups.Count == 0)
                    return await Task.FromResult($"查询到{recommendFood.Name} 不是多规格商品,要按默认加入购物车吗？");

                if (intentValue == "Specification" && recommendFood.ParameterGroups.Count > 0)
                {
                    return await HandleSpecialCommentWhenBelongSpecificationAsync(recommendFood, specialComment, merchId);
                }
            }

            askFoodHistoryDic[recommendFood.Id.ToString()] = recommendFood;
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

                specificationFoodDic[recommendFood.Id.ToString()] = recommendFood;
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

    public async Task<string> AddSpecificationsFoodsync(string specificationsName, string latestChatHistory)
    {
        var categories = specificationFoodDic.Values.SelectMany(x => x.ParameterGroups).SelectMany(x => x.ParameterItems).Select(x => x.Name).ToList();
        var askGptResult = await AskGptAsync(GetSpecificationsRequest(specificationsName, categories));

        if (string.IsNullOrWhiteSpace(askGptResult.Data.Response) || askGptResult.Data.Response.Contains("NONE"))
        {
            var questionIntentResponse = await AskGptAsync(GetQuestionIntentRequest(specificationsName, latestChatHistory));
            return await PolishQuestionIntentAsync(specificationsName, questionIntentResponse.Data.Response, latestChatHistory, IntentScenes.Specification);
        }

        var foodItemName = askGptResult.Data.Response.Split(":")[1];
        var foodItem = specificationFoodDic.Values.SelectMany(x => x.ParameterGroups).SelectMany(x => x.ParameterItems).FirstOrDefault(x => x.Name.Trim().Contains(foodItemName.Trim()));
        var food = specificationFoodDic.Values.FirstOrDefault(x => x.ParameterGroups.Any(t => t.Id == foodItem.GroupId));
        var foodParameterMap = new FoodParameterMapDto { FoodId = food.Id, ParameterItemId = foodItem.Id };

        if (foodParameterMap == null && foodParameterMap.FoodId != Guid.Empty)
            throw new Exception("Response mapping異常:" + askGptResult.Data.Response);

        var foodObj = specificationFoodDic[foodParameterMap.FoodId.ToString()];
        if (foodObj != null)
        {
            var foodParameter = specificationFoodDic[foodParameterMap.FoodId.ToString()];
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

            specificationFoodDic = new Dictionary<string, MerchFoodDto>();
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
                RecommendCount = 2
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

    private AskGptRequest GetQuestionIntentRequest(string question, string chatHistory)
    {
        return new AskGptRequest
        {
            Model = 6,
            Messages = new List<AskSmartiesMessageDto>
            {
                new()
                {
                    Role = "system",
                    Content = "You are a helpful assistant for intent classification,you can understand cantonese and mandarin, you can classify the user Text into one of these intents, " +
                              "Intents: [\"NONE\",\"AskForAddress\",\"GetActivity\",\"CheckParkingLotExists\",\"IntroducingRecommendedDishes\",\"AddOrder\",\"AddCart\",\"AskFoodDetail\",\"OrderDetail\"],  " +
                              "you SHOULD ONLY answer if you are very sure, otherwise reply ''Intent: NONE''." +
                              "These are the positive examples: Samples:[\"你好\",\"中国有哪些特色美食\",\"如何学习编程\",\"谈谈你对中美关系的理解\"] Intent: NONE " +
                              "\n\n Samples:[\"餐厅地址在哪里\",\"请问\\\"店铺\\\"在哪里\"] Intent: AskForAddress " +
                              "\n\n Samples:[\"获取活动\",\"最近有什么活动\",\"帮我查询下最近的活动\"] Intent: GetActivity " +
                              "\n\n Samples:[\"有没有停车场呀\",\"能不能停车呀\"] Intent: CheckParkingLotExists " +
                              "\n\n Samples:[\"有什么菜可以介绍下吗\",\"帮我介绍下招牌菜\",\"我不知道吃什么，有什么推荐吗\",\"有无特价菜\",\"推荐下招牌菜\"] Intent: IntroducingRecommendedDishes " +
                              "\n\n Samples:[\"下单\",\"埋单\",\"落单\",\"结算\"] Intent: AddOrder " +
                              "\n\n Samples:[\"帮我落个蛋炒饭\",\"我要鸡腿饭\",\"来个牛肉饭\",\"要一份\"] Intent: AddCart " +
                              "\n\n Samples:[\"有无蛋炒饭\",\"三明治多少钱\",\"烧鸭怎么卖\"] Intent: AskFoodDetail " +
                              "\n\n Samples:[\"订单详情\",\"看看我买了什么\"] Intent: OrderDetail " +
                              "\n\n These are the navigate examples: Samples:[\"能停车多久呀\",\"有多少停车位呀\",\"什么时候开放呀\",\"这碟菜加葱吗\"," +
                              "\"有饮料提供吗\",\"有厕所吗\",\"有洗手间吗\",\"有婴儿座位吗\",\"店铺能坐多少人\",\"好不好吃\",\"菜的口味是怎么样的\",\"有什么其他配菜\"," +
                              "\"菜品辣不辣？\",\"菜品的烹饪方式是怎么样？\",\"菜品的做法\",\"点整\",\"怎么煮\",\"停车场在哪里\",\"暂无停车场\"] Intent: NONE"
                },
                new()
                {
                    Role = "system",
                    Content = $"上下文:{chatHistory}"
                },
                new()
                {
                    Role = "user",
                    Content = $"输入:{question}"
                }
            }
        };
    }

    private AskGptRequest GetFoodSpecificationOrSpecialCommentRequest(string input,string chatHistory)
    {
        return new AskGptRequest
        {
            Model = 6,
            Messages = new List<AskSmartiesMessageDto>
            {
                new()
                {
                    Role = "system",
                    Content = "You are a helpful assistant for intent classification,you can understand cantonese and mandarin, you can classify the user Text into one of these intents, " +
                              "Intents: [\"NONE\",\"Specification\",\"SpecialComment\"],  " +
                              "you SHOULD ONLY answer if you are very sure, otherwise reply ''Intent: NONE''." +
                              "These are the examples:" +
                              "\n\n Samples:[\"搭配意粉\",\"中份\",\"小份\",\"大份\",\"配咖啡\",\"红茶\",\"冷饮\",\"热饮\",\"大杯\",\"白饭\"] Intent: Specification " +
                              "\n\n Samples:[\"咸一点\",\"辣一点\",\"加辣\",\"走葱\",\"甜一点\",\"咸一点\",\"加多个手套\",\"两个筷子\"] Intent: SpecialComment "
                },
                new()
                {
                    Role = "system",
                    Content = $"上下文:{chatHistory}"
                },
                new()
                {
                    Role = "user",
                    Content = $"输入:{input}"
                }
            }
        };
    }

    private AskGptRequest GetFoodAssistantAnswerRequest(string input,string chatHistory)
    {
        return new AskGptRequest
        {
            Model = 6,
            Messages = new List<AskSmartiesMessageDto>
            {
                new()
                {
                    Role = "system",
                    Content = "你是一个对餐厅下单有高度理解力的人工智能,你能理解粤语和普通话,我希望你能够根据用户所说的内容来作出专业的回答，但是如果涉及到命令式的操作，你应该拒绝对方。你的回答一定是你的原话，同时要简短精炼，不需要加上“回复”，“回答”。不能有虚构内容。"
                },
                new()
                {
                    Role = "system",
                    Content = $"上下文:{chatHistory}"
                },
                new()
                {
                    Role = "user",
                    Content = $"输入:{input}"
                }
            }
        };
    }

    private AskGptRequest GetFoodDetailRequest(string input, string chatHistory)
    {
        return new AskGptRequest
        {
            Model = 6,
           // ResponseFormat = new ResponseFormat { Type = "json_object" },
            Messages = new List<AskSmartiesMessageDto>
            {
                new()
                {
                    Role = "system",
                    Content = "你是一个对餐厅下单有高度理解力的人工智能,我希望你能够根据用户所说的内容来推断出顾客想要下单的菜品和菜品数量，" +
                              "以及对菜品特别的要求，我也希望你能够理解并且能够匹配到菜单里面的产品，如果匹配不到就抽取你所理解的菜品名；如果菜名含有中英文，你只需要提取中文那部分合理的菜名；注意返回的一定是个数组；提出的内容全部转中文繁体；" +
                              "你的輸出格式一定要符合這個JSON: [{\"foodName\": \"菜名\", \"quantity\": 2, \"specialRequirement\": \"走蔥\"}]，" +
                              "对于specialRequirement的提取有些例子你可以参考下：" +
                              "\n\n 输入：有奶茶吗？我要个中杯的；specialRequirement:中杯" +
                              "\n\n 输入：有沙爹牛肉吗？配冻咖啡给我；specialRequirement:冻咖啡"
                },
                new()
                {
                    Role = "system",
                    Content = $"上下文:{chatHistory}"
                },
                new()
                {
                    Role = "user",
                    Content = $"输入:{input}"
                }
            }
        };
    }

    private AskGptRequest GetSpecificationsRequest(string input, List<string> categories)
    {
        return new AskGptRequest
        {
            Model = 6,
            Messages = new List<AskSmartiesMessageDto>
            {
                new()
                {
                    Role = "system",
                    Content =
                        "You are a great helper in text classification, you can classify user text into one of these categories," +
                        $"Categories: {JsonConvert.SerializeObject(categories)}," +
                        " you SHOULD ONLY answer if you are very sure, otherwise reply ''category: NONE''.",
                },
                new()
                {
                    Role = "user",
                    Content = "输入:" + input
                }
            }
        };
    }

    private async Task<string> HandleSpecialCommentWhenBelongSpecificationAsync(MerchFoodDto recommendFood, string specialComment,
        Guid merchId)
    {
        var categories = recommendFood.ParameterGroups.SelectMany(x => x.ParameterItems).Select(x => x.Name).ToList();
        var askGptResult = await AskGptAsync(GetSpecificationsRequest(specialComment, categories));
        var categoryName = askGptResult.Data.Response.Split(":")[1];
        var foodItemName = FindCommonSubstring(specialComment, categoryName);

        var sb = new StringBuilder();
        //特殊要求存在商品规格里面，帮用户选择，并看看有没有下一个规格要选择的，没有的话，直接加入购物车，有的话，继续让用户选
        if (recommendFood.ParameterGroups.SelectMany(x => x.ParameterItems).Select(x => x.Name)
            .Any(x => !string.IsNullOrWhiteSpace(foodItemName) && x.Contains(foodItemName)))
        {
            var foodItem = recommendFood.ParameterGroups.SelectMany(x => x.ParameterItems)
                .FirstOrDefault(x => x.Name.Trim().Contains(foodItemName.Trim()));
            var foodGroup = recommendFood.ParameterGroups.FirstOrDefault(x => x.Id == foodItem.GroupId);
            foodGroup.IsAnswer = foodItem.IsSelected = true;

            //没有下一个规格要选择的， 直接加入购物车
            if (recommendFood.ParameterGroups.All(x => x.IsAnswer))
            {
                var selectedFoodParamList = recommendFood.ParameterGroups.Where(x => x.IsAnswer)
                    .Select(x => x.ParameterItems.First(t => t.IsSelected)).Select(x => new FoodParameterDto
                    {
                        ParameterId = x.Id, Quantity = 1, ParameterGroupId = x.GroupId
                    }).ToList();
                await AddToCartAsync(merchId, recommendFood, 1, selectedFoodParamList);

                sb.Append($"好的，已将规格为 {foodItem.Name}的{recommendFood.Name} 加入购物车，请问还有什么可以帮到你吗？");
            }
            else //有的话，继续让用户选
            {
                var needSelectedFoodGroup = recommendFood.ParameterGroups.FirstOrDefault(x => !x.IsAnswer);

                sb.Append(
                    $"好的，已帮你选择规格为 {foodItem.Name}的{recommendFood.Name} \n 在{needSelectedFoodGroup.Name}规格方面还有以下需要选择：");
                foreach (var item in needSelectedFoodGroup.ParameterItems)
                {
                    sb.Append($"[{item.Name}，价格：{item.Price}] ,");
                }

                sb.Append("\n 请问你需要哪一个？");
            }
        }
        else //特殊要求不存在商品规格里面
        {
            sb.Append($"查询到{recommendFood.Name} 没有搭配{specialComment} 的哦，");
            var parameterGroup = recommendFood.ParameterGroups.First();
            sb.Append($"\n 你可以选择 {parameterGroup.Name} 来搭配，分别有：");
            foreach (var item in parameterGroup.ParameterItems)
            {
                sb.Append($"[{item.Name}] ,");
            }

            sb.Append(" 请问需要选择哪个规格呢？");
        }

        specificationFoodDic[recommendFood.Id.ToString()] = recommendFood;
        return await Task.FromResult(sb.ToString());
    }

    static string FindCommonSubstring(string s1, string s2)
    {
        int[,] dp = new int[s1.Length + 1, s2.Length + 1];
        int maxLength = 0;
        int endIndex = 0;

        for (int i = 1; i <= s1.Length; i++)
        {
            for (int j = 1; j <= s2.Length; j++)
            {
                if (s1[i - 1] == s2[j - 1])
                {
                    dp[i, j] = dp[i - 1, j - 1] + 1;
                    if (dp[i, j] > maxLength)
                    {
                        maxLength = dp[i, j];
                        endIndex = i - 1;
                    }
                }
                else
                {
                    dp[i, j] = 0;
                }
            }
        }

        if (maxLength == 0)
        {
            return "";
        }

        return s1.Substring(endIndex - maxLength + 1, maxLength);
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
