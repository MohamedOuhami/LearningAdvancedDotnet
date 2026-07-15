public class FakeEmailSender : IEmailSender
{
    private readonly IUserContext _IUserContext;
    public FakeEmailSender(IUserContext userContext) => _IUserContext = userContext;
    public void SendEmail(string message)
    {
        System.Console.WriteLine($"Sending email from {_IUserContext.CurrentUser}, with body : {message}");
    }
}