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
using CopilotChat.WebApi.Storage;
using DocumentFormat.OpenXml.Office.CustomUI;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;

namespace CopilotChat.WebApi.Plugins.Yesmeal;

public class PhoneCallPlugin
{
    private static IHttpClientFactory _httpClientFactory;
    private static ThirdPartyTokenOptions _thirdPartyTokenOptions;
    private static Dictionary<string,MerchFoodDto> merchFoodDic = new Dictionary<string, MerchFoodDto>();
    public PhoneCallPlugin(IHttpClientFactory httpClientFactory, IOptions<ThirdPartyTokenOptions> thirdPartyTokenOptions)
    {
        _httpClientFactory = httpClientFactory;
        _thirdPartyTokenOptions = thirdPartyTokenOptions.Value;
    }

    [KernelFunction, Description("Get campaign/activities of merchant，restaurant,")]
    [return: Description("The campaign/activities details")]
    public static async Task<string> GetMerchantCampaign(
        KernelArguments args)
    {
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
    public static async Task<string> GetMerchantAddress(
        KernelArguments args)
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
    public static async Task<string> GetMerchantParkingInfo(
        KernelArguments args)
    {
        return await Task.FromResult("暂无停车场");
    }

    [KernelFunction, Description("customer want to eat or order specific dish name, for example, milk, tea, rice, sandwich, chicken chop, pork chop, lunch meat, egg, fish, ham, noodles, porridge, vegetable")]
    public static async Task<string> AskForFoodDetail(
        [Description("the name of food")]string foodName,
        [Description("the quantity of food")]string quantity,
        [Description("the special comment of food")]string specialComment,
        KernelArguments args)
    {
        Console.WriteLine("hit the AskForFoodDetail:" + foodName);

        return await AsyncUtils.SafeInvokeAsync<string>(async () =>
        {
            var merchId = Guid.Parse("3bd51ea0-9b3e-45f2-92b7-c30fb162f910");
            var recommendFood = await GetRecommendFoodAsync(merchId, foodName);
            if (recommendFood == null) return $"暂无{foodName},请换一个好吗？";

            if (recommendFood.ParameterGroups.Count == 0)
            {
                var resultTemplate = $"查询到{recommendFood.Name},价钱：{recommendFood.Price},已帮你加入购物车。需要埋单吗？";

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
    public static async Task<string> GetMerchantOrderDetailAsync()
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
        for (var i = 0; i < orderDetailForMerch.ShoppingCart.ShoppingCartItems.Count; i++)
        {
            var item = orderDetailForMerch.ShoppingCart.ShoppingCartItems[i];
            var parameterFoodDesc = item.ShoppingCartItemParams.Any() ? item.ShoppingCartItemParams.FirstOrDefault()?.Name : " ";
            result.Append($"{i + 1}.{item.FoodName} {parameterFoodDesc}---单价：{item.Price} ---数量:{item.Quantity}；");
        }
        result.Append($"\n 总金额：{orderDetailForMerch.ShoppingCart.CartTotal}");
        return result.ToString();
    }

    [KernelFunction, Description("customer want to place an order")]
    public static async Task<string> AddOrderByMerchIdAsync()
    {
        var merchId = Guid.Parse("3bd51ea0-9b3e-45f2-92b7-c30fb162f910");
        using var  httpClient = CreateYesmealHttpClient();

        var httpContent = new StringContent(JsonConvert.SerializeObject(new
            AddOrderByMerchIdRequest
            {
                MerchId = merchId
            }));
        httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var response = await httpClient.PostAsync("https://testapi.yesmeal.com/api/order/by/phonecall",httpContent)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AddOrderByMerchIdResponse>();

        return $"下单成功，你的取餐号为：{result.MealCode}，请在{result.PickupTime}左右到店pick up，多谢。";
    }

    [KernelFunction, Description("customers have selected the respective product/food specifications.")]
    [return: Description("Please be careful not to alter the returned original text information")]
    public static async Task<string> AddSpecificationsFoodsync([Description("Food items with specified specifications")]string specificationsName)
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
        var foodParameterMap = await AskGptAsync(askGptRequest);

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
            return await Task.FromResult("好的，已帮你加入购物车。请问还需要其他吗？还是埋单吗？");
        }

        return await Task.FromResult("抱歉，系统开了小差，请再说多一次好吗？");
    }

    private static async Task<MerchFoodDto> GetRecommendFoodAsync(Guid merchId, string foodName = null)
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

    private static async Task<string> Translation(string content)
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

    private static async Task<FoodParameterMapDto> AskGptAsync(AskGptRequest request)
    {
        return await AsyncUtils.SafeInvokeAsync<FoodParameterMapDto>(async () =>
        {
            using var httpClient = CreateSmartiesHttpClient();
            var httpContent = new StringContent(JsonConvert.SerializeObject(request));
            httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            var response = await httpClient.PostAsync("https://testsmarties.yamimeal.ca/api/Ask/general/query", httpContent)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var askGptResult = await response.Content.ReadFromJsonAsync<AskGptResponse>();
            var foodParamMap = JsonConvert.DeserializeObject<FoodParameterMapDto>(askGptResult.Data.Response);
            if (foodParamMap != null && foodParamMap.FoodId != Guid.Empty)
                return foodParamMap;
            throw new Exception("Response mapping異常:" + askGptResult.Data.Response);
        },nameof(AskGptAsync));
    }

    private static async Task<string> AddToCartAsync(Guid merchId, MerchFoodDto merchFood,int quantity,
        List<FoodParameterDto>? foodParameters = null)
    {
        using var httpClient = CreateYesmealHttpClient();

        var httpContent = new StringContent(JsonConvert.SerializeObject(new
            AddOrUpdateItemToCartRequest
            {
                MerchId = merchId,
                FoodId = merchFood.Id,
                Quantity = quantity,
                FoodParameters = foodParameters,
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
    public static async Task EmptyCartAsync()
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

    private static HttpClient CreateYesmealHttpClient(Dictionary<string, string>? headers = null)
    {
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_thirdPartyTokenOptions.Yesmeal}");
        httpClient.DefaultRequestHeaders.Add("Source_System",
            headers == null || !headers.ContainsKey("SourceSystem") ? "1" : headers["SourceSystem"]);
        httpClient.DefaultRequestHeaders.Add("language_code",
            headers == null || !headers.ContainsKey("LanguageCode") ? "zh-TW" : headers["LanguageCode"]);
        return httpClient;
    }

    private static HttpClient CreateSmartiesHttpClient()
    {
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_thirdPartyTokenOptions.Smarties}");
        httpClient.DefaultRequestHeaders.Add("accept",  "text/plain");
        return httpClient;
    }
}
