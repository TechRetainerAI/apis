namespace MeDan.Api.Models;

/// <summary>Top-level role of a platform user.</summary>
public enum UserRole
{
    Student = 0,
    Owner = 1,      // owns a Company (hostel business)
    Worker = 2,     // staff of a Company, can post/manage listings
    Manager = 3,    // platform-side manager (optional)
    Admin = 4
}

/// <summary>Kind of accommodation a listing represents.</summary>
public enum PropertyType
{
    Hostel = 0,
    Hometel = 1,
    Apartment = 2,
    SelfContained = 3,
    Hall = 4
}

/// <summary>Layout/sharing style of a room.</summary>
public enum RoomType
{
    Single = 0,        // 1 in a room
    DoublyShared = 1,  // 2 in a room
    TriplyShared = 2,  // 3 in a room
    QuadShared = 3,    // 4 in a room
    Ensuite = 4,
    Apartment = 5
}

public enum RoomStatus
{
    Available = 0,
    Occupied = 1,   // all beds taken (app calls this "occupied")
    Maintenance = 2
}

/// <summary>Who a room/bed is for.</summary>
public enum Gender
{
    Male = 0,
    Female = 1,
    Mixed = 2
}

public enum BedStatus
{
    Available = 0,
    Reserved = 1,   // held by a pending booking
    Occupied = 2    // student checked in
}

/// <summary>Escrow state machine for a booking.</summary>
public enum BookingStatus
{
    Pending = 0,
    PaymentHeld = 1,
    CheckedIn = 2,
    Completed = 3,
    Disputed = 4,
    Refunded = 5,
    Cancelled = 6
}

public enum PaymentChannel
{
    MomoMtn = 0,
    MomoTelecel = 1,
    Card = 2
}

public enum PaymentStatus
{
    Initialized = 0,
    Success = 1,
    Failed = 2,
    Abandoned = 3
}

/// <summary>Subscription tier of a hostel business (drives commission + listing limits).</summary>
public enum CompanyTier
{
    Starter = 0,   // free, 7.0% commission, max 5 listings
    Pro = 1,       // GH₵30/mo, 5.5% commission, unlimited
    Premium = 2    // GH₵80/mo, 5.0% commission, unlimited
}

/// <summary>Role of a user inside a company.</summary>
public enum CompanyRole
{
    Owner = 0,
    Manager = 1,
    Worker = 2
}

/// <summary>How support closed a disputed booking.</summary>
public enum DisputeOutcome
{
    /// <summary>Student is refunded; the bed goes back on the market.</summary>
    Refund = 0,
    /// <summary>Dispute rejected; escrow releases to the owner.</summary>
    Release = 1
}

public enum ReferralStatus
{
    Pending = 0,
    Claimed = 1,
    Paid = 2
}
