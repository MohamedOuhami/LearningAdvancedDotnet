public class OrderService
{
    private readonly IEmailSender _emailSender; 

    public OrderService(IEmailSender emailSender)
    {
        _emailSender = emailSender;
    }

    public void TakeOrder()
    {
        _emailSender.SendEmail("Order done");
    }
}