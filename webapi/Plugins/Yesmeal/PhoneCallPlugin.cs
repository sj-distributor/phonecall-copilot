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
using DocumentFormat.OpenXml.Office.CustomUI;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;

namespace CopilotChat.WebApi.Plugins.Yesmeal;

public class PhoneCallPlugin
{
    private static IHttpClientFactory _httpClientFactory;
    private static ThirdPartyTokenOptions _thirdPartyTokenOptions;
    public PhoneCallPlugin(IHttpClientFactory httpClientFactory, IOptions<ThirdPartyTokenOptions> thirdPartyTokenOptions)
    {
        _httpClientFactory = httpClientFactory;
        _thirdPartyTokenOptions = thirdPartyTokenOptions.Value;
    }

    [KernelFunction, Description("Get merchant campaign, activities")]
    [return: Description("The campaign details")]
    public static async Task<string> GetMerchantCampaign(
        KernelArguments args)
    {
        return await AsyncUtils.SafeInvokeAsync<string>(async () =>
        {
            var httpClient = CreateYesmealHttpClient();

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

    [KernelFunction, Description("Get merchant address, location")]
    public static async Task<string> GetMerchantAddress(
        KernelArguments args)
    {
        return await AsyncUtils.SafeInvokeAsync<string>(async () =>
        {
            var httpClient = CreateYesmealHttpClient();

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
            var recommendFood = await SearchSimilarFoodAsync(merchId,foodName);
            if (recommendFood == null) return $"暂无{foodName},请换一个好吗？";

            var resultTemplate = $"查询到{recommendFood.Name},价钱：{recommendFood.Price},已帮你加入购物车。需要埋单吗？";

            await AddToCartAsync(merchId, recommendFood.Id, 1);
            Console.WriteLine(JsonConvert.SerializeObject(recommendFood));

            return await Task.FromResult(resultTemplate);
        }, nameof(GetMerchantAddress));
    }

    [KernelFunction, Description("customer want to check order detail")]
    public static async Task<string> GetMerchantOrderDetailAsync()
    {
        var merchId = Guid.Parse("3bd51ea0-9b3e-45f2-92b7-c30fb162f910");
        var httpClient = CreateYesmealHttpClient();

        var response = await httpClient.GetAsync($"https://testapi.yesmeal.com/api/shoppingcart/bymerch?merchid={merchId}")
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var orderDetailForMerch = await response.Content.ReadFromJsonAsync<GetCurrentUserShoppingCartByMerchIdResponse>();
        if (orderDetailForMerch?.ShoppingCart == null || !orderDetailForMerch.ShoppingCart.ShoppingCartItems.Any())
            return "你的购物车暂时没商品，请先进行下单，谢谢";
        var result = new StringBuilder();
        for(var i=0;i< orderDetailForMerch.ShoppingCart.ShoppingCartItems.Count;i++)
        {
            var item = orderDetailForMerch.ShoppingCart.ShoppingCartItems[i];
            result.Append($"{i+1}.{item.FoodName}---单价：{item.Price}---数量:{item.Quantity}；");
        }
        result.Append($"总金额：{orderDetailForMerch.ShoppingCart.CartTotal}");
        return result.ToString();
    }

    [KernelFunction, Description("customer want to place an order")]
    public static async Task<string> AddOrderByMerchIdAsync()
    {
        var merchId = Guid.Parse("3bd51ea0-9b3e-45f2-92b7-c30fb162f910");
        var httpClient = CreateYesmealHttpClient();

        var httpContent = new StringContent(JsonConvert.SerializeObject(new
            AddOrderByMerchIdRequest
            {
                MerchId = merchId
            }));

        var response = await httpClient.PostAsync("https://testapi.yesmeal.com/api/order/by/phonecall",httpContent)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AddOrderByMerchIdResponse>();

        return $"下单成功，你的取餐号为：{result.MealCode}，请在{result.PickupTime}左右到店pick up，多谢。";
    }

    private static async Task<MerchFoodDto> SearchSimilarFoodAsync(Guid merchId, string foodName)
    {
        return await AsyncUtils.SafeInvokeAsync<MerchFoodDto>(async () =>
        {
            var httpClient = CreateYesmealHttpClient(new Dictionary<string, string>{ {"LanguageCode", "zh-TW"}});
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
        }, nameof(SearchSimilarFoodAsync));
    }

    private static async Task<string> Translation(string content)
    {
        var httpClient = CreateSmartiesHttpClient();

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

    private static async Task<AskGptResponse> AskGptAsync(AskGptRequest request)
    {
        var httpClient = CreateSmartiesHttpClient();

        var httpContent = new StringContent(JsonConvert.SerializeObject(request));

        var response = await httpClient.PostAsync("https://smarties.yamimeal.ca/api/ask/general/query",httpContent)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<AskGptResponse>();
    }

    private static async Task<string> AddToCartAsync(Guid merchId, Guid foodId,int quantity)
    {
        var httpClient = CreateYesmealHttpClient();

        var httpContent = new StringContent(JsonConvert.SerializeObject(new
            AddOrUpdateItemToCartRequest
            {
                MerchId = merchId,
                FoodId = foodId,
                Quantity = quantity,
                FoodParameters = new List<FoodParameterDto>(),
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

    private static async Task EmptyCartAsync()
    {
        var httpClient = CreateYesmealHttpClient();

        var httpContent = new StringContent(JsonConvert.SerializeObject(new
            EmptyShoppingCartRequest
            {
                MerchId = Guid.Parse("3bd51ea0-9b3e-45f2-92b7-c30fb162f910"),
                ShouldThrowGroupifyError = false,
                ShouldExcludePickupOrFallbackMerchants = false
            }));

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
