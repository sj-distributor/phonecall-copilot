// Copyright (c) Microsoft. All rights reserved.

using System;

namespace CopilotChat.WebApi.Dtos;

public class MerchDto
{
    public Guid Id { get; set; }

    public string Name { get; set; }
    public int Status { get; set; }
    public string Address { get; set; }
    public decimal? Lat { get; set; }
    public decimal? Lng { get; set; }

    public MerchParkingLotPickupSupportType MerchParkingLotPickupSupportType { get; set; }
}

public enum MerchParkingLotPickupSupportType
{
    NotSupported,
    ParkingLotOnly,
    OptionalParkingLot
}
