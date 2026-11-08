using Speranta.Domain;

namespace Speranta.Api.Contracts;

/// <summary>
/// The Romanian words the church approved, used where the API itself writes prose
/// (history lines, notification emails). The web client keeps its own copy for
/// the screens.
/// </summary>
public static class Labels
{
    public static string Of(OrderStatus s) => s switch
    {
        OrderStatus.New => "Nou",
        OrderStatus.Confirmed => "Confirmat",
        OrderStatus.Delivered => "Livrat",
        OrderStatus.PickedUp => "Ridicat",
        OrderStatus.Cancelled => "Anulat",
        _ => s.ToString(),
    };

    public static string Of(SignupStatus s) => s switch
    {
        SignupStatus.New => "Nou",
        SignupStatus.Contacted => "Contactat",
        SignupStatus.Enrolled => "Înscris",
        SignupStatus.Withdrawn => "Renunțat",
        _ => s.ToString(),
    };

    public static string Of(MessageStatus s) => s switch
    {
        MessageStatus.New => "Nou",
        MessageStatus.Answered => "Răspuns",
        _ => s.ToString(),
    };

    public static string Of(DeliveryMethod d) => d switch
    {
        DeliveryMethod.Delivery => "Livrare la domiciliu",
        DeliveryMethod.Pickup => "Ridicare de la biserică",
        _ => d.ToString(),
    };

    public static string Of(AgeGroup a) => a switch
    {
        AgeGroup.Under20 => "Sub 20",
        AgeGroup.From20To30 => "20–30",
        AgeGroup.From30To40 => "30–40",
        AgeGroup.From40To50 => "40–50",
        AgeGroup.From50To70 => "50–70",
        AgeGroup.Over70 => "Peste 70",
        _ => a.ToString(),
    };

    public static string YesNo(bool value) => value ? "Da" : "Nu";
}
