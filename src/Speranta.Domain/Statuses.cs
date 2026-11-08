namespace Speranta.Domain;

// Code names are English; the clients show the Romanian labels the church
// approved (Nou, Confirmat, Livrat, Ridicat, Anulat …). Stored as text, so a
// person reading the table sees the same words as the API.

/// <summary>A free book order: Nou → Confirmat → Livrat | Ridicat, or Anulat.</summary>
public enum OrderStatus
{
    New,
    Confirmed,
    Delivered,
    PickedUp,
    Cancelled,
}

/// <summary>A kids' activity sign-up: Nou → Contactat → Înscris, or Renunțat.</summary>
public enum SignupStatus
{
    New,
    Contacted,
    Enrolled,
    Withdrawn,
}

/// <summary>A contact message: Nou → Răspuns.</summary>
public enum MessageStatus
{
    New,
    Answered,
}

/// <summary>
/// How the person receives the book. The site form always asks for delivery; an
/// admin switches it to pickup when that is agreed on the phone.
/// </summary>
public enum DeliveryMethod
{
    Delivery,
    Pickup,
}

/// <summary>The age brackets the old WordPress form offered, unchanged.</summary>
public enum AgeGroup
{
    Under20,
    From20To30,
    From30To40,
    From40To50,
    From50To70,
    Over70,
}

public enum AnnouncementStatus
{
    Draft,
    Published,
}

public enum SubmissionKind
{
    BookOrder,
    KidsSignup,
    ContactMessage,
}
