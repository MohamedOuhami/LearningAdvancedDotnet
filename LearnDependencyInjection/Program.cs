using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

services.AddScoped<IEmailSender,FakeEmailSender>();
services.AddScoped<IUserContext,UserContext>();
services.AddScoped<OrderService>();

var provider = services.BuildServiceProvider(new ServiceProviderOptions {ValidateScopes = true});
using (var scope1 = provider.CreateScope())
{
    var ctx = scope1.ServiceProvider.GetRequiredService<IUserContext>();
    ctx.CurrentUser = "Alice";
    scope1.ServiceProvider.GetRequiredService<OrderService>().TakeOrder();
}

using (var scope2 = provider.CreateScope())
{
    var ctx = scope2.ServiceProvider.GetRequiredService<IUserContext>();
    ctx.CurrentUser = "Bob";
    scope2.ServiceProvider.GetRequiredService<OrderService>().TakeOrder();
}

