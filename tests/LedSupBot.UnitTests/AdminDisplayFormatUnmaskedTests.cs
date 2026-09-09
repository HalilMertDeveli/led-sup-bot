using LedSupBot.Application.Admin;
using LedSupBot.Domain.Entities;
using Xunit;

namespace LedSupBot.UnitTests;

public sealed class AdminDisplayFormatUnmaskedTests
{
    [Fact]
    public void MaskPhone_Returns_Unmasked_Phone_Number()
    {
        var phone = "905425192119";
        var displayed = AdminDisplayFormat.MaskPhone(phone);
        Assert.Equal("905425192119", displayed);
    }

    [Fact]
    public void MaskAnyDesk_Returns_Unmasked_Address()
    {
        var anydesk = "123 456 789";
        var displayed = AdminDisplayFormat.MaskAnyDesk(anydesk);
        Assert.Equal("123 456 789", displayed);
    }

    [Fact]
    public void FormatCustomerIdentifier_Returns_DisplayName_Or_Full_Phone()
    {
        var customer1 = new Customer { PhoneNumber = "905425192119", DisplayName = "Ahmet Yılmaz" };
        Assert.Equal("Ahmet Yılmaz", AdminDisplayFormat.FormatCustomerIdentifier(customer1));

        var customer2 = new Customer { PhoneNumber = "905425192119", DisplayName = null };
        Assert.Equal("905425192119", AdminDisplayFormat.FormatCustomerIdentifier(customer2));
    }
}
