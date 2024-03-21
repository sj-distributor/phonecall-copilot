using System;
using System.Collections.Generic;

namespace CopilotChat.WebApi.Dtos;

public class CampaignDto
{
    public Guid Id { get; set; }

    public List<MerchCouponPromotionDto> MerchCouponPromotions { get; set; }
}
