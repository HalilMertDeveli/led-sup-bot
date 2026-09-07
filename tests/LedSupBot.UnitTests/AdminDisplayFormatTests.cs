using LedSupBot.Application.Admin;
using LedSupBot.Domain.Entities;

namespace LedSupBot.UnitTests;

public sealed class AdminDisplayFormatTests
{
    [Fact]
    public void Saved_contact_name_wins_over_phone() =>
        Assert.Equal("Ahmet LED", AdminDisplayFormat.FormatCustomerIdentifier(
            new Customer { PhoneNumber = "905551234567", DisplayName = "Ahmet LED" }));

    [Fact]
    public void Unsaved_contact_uses_phone() =>
        Assert.Equal("905551234567", AdminDisplayFormat.FormatCustomerIdentifier(
            new Customer { PhoneNumber = "905551234567" }));

    [Fact]
    public void Unmapped_lid_is_not_exposed_or_fabricated() =>
        Assert.Equal("Telefon numarası çözümleniyor", AdminDisplayFormat.FormatCustomerIdentifier(
            new Customer { PhoneNumber = "123456789@lid" }));
}
