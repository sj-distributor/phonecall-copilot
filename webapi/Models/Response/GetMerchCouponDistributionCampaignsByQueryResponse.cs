// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using CopilotChat.WebApi.Dtos;

namespace CopilotChat.WebApi.Models.Response;

public class GetMerchCouponDistributionCampaignsByQueryResponse
{
    public List<CampaignDto> Campaigns { get; set; }
}
