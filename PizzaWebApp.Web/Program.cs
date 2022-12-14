using Microsoft.EntityFrameworkCore;
using PizzaWebApp.DAL.EfStructures;
using PizzaWebApp.Models.Entities;
using PizzaWebApp.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
    });
builder.Services.AddAuthorization();

builder.Services.AddDbContext<PizzaWebAppDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddSingleton<Cart>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.Map("/home", () => Results.Redirect("/"));
app.Map("/cart", [Authorize] async (HttpContext context) =>
    await context.Response.WriteAsync(File.ReadAllText("wwwroot/cart.html")));
app.Map("/signup", async (HttpContext context) =>
    await context.Response.WriteAsync(File.ReadAllText("wwwroot/signup.html")));

app.MapPost("/signup", (HttpContext context, PizzaWebAppDbContext db) =>
{
    var form = context.Request.Form;
    if (string.IsNullOrWhiteSpace(form["firstname"]) ||
        string.IsNullOrWhiteSpace(form["lastname"]) ||
        string.IsNullOrWhiteSpace(form["email"]) ||
        string.IsNullOrWhiteSpace(form["password"]))
    {
        return Results.BadRequest("The field(s) is empty");
    }

    var customer = db.Customers.FirstOrDefault(c => c.Email == form["email"].ToString());
    if (customer is not null) return Results.Problem("This user already exists");

    customer = new Customer
    {
        FirstName = form["firstname"].ToString(),
        LastName = form["lastname"].ToString(),
        Email = form["email"].ToString(),
        Password = form["password"].ToString(),
    };

    db.Customers.Add(customer);
    db.SaveChanges();

    return Results.Redirect("/");
});

app.MapGet("/api/menu", async (PizzaWebAppDbContext db) => await db.Pizzas.ToListAsync());
app.MapGet("/api/cart", (Cart cart) => cart);
app.MapGet("/api/cart/price", (Cart cart) => cart.Price);


app.MapPost("/api/menu/{id:int}", [Authorize]async (int id, Cart cart, PizzaWebAppDbContext db) =>
{
    Pizza? pizza = await db.Pizzas.FirstOrDefaultAsync(p => p.PizzaId == id);
    if (pizza == null) return Results.NotFound(new { Message = "Not found" });

    cart.AddItem(pizza);

    return Results.Json(pizza);
});

app.MapDelete("/api/cart/{id:int}", (int id, Cart cart) =>
{
    Pizza? pizza = cart.FirstOrDefault(p => p.PizzaId == id);
    if (pizza == null) return Results.NotFound(new { Message = "Not found" });

    cart.RemoveItem(pizza);

    return Results.Json(pizza);
});

app.MapPost("/api/cart/payment", async(Cart cart, HttpContext context, PizzaWebAppDbContext db) =>
{
    if (cart.Any() == false)
    {
        return Results.BadRequest(new { Message = "Cart is empty"});
    }

    var login = context.User.Identity?.Name;
    Customer customer = await db.Customers.FirstAsync(c => c.Email == login);

    var order = new Order
    {
        CustomerId = customer.CustomerId,
        OrderDate = DateTime.Now,
    };

    db.Orders.Add(order);
    db.SaveChanges();

    foreach (var item in cart)
    {
        var payment = new Payment
        {
            OrderId = order.OrderId,
            PizzaId = item.PizzaId,
            PaymentDate = DateTime.Now,
        };
        db.Payments.Add(payment);
    }

    db.SaveChanges();

    return Results.Json(cart);
});

app.MapGet("/login", async (HttpContext context) =>
    await context.Response.WriteAsync(File.ReadAllText("wwwroot/login.html")));

app.MapGet("/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

app.MapPost("/login", async (HttpContext context, PizzaWebAppDbContext db) =>
{
    var form = context.Request.Form;

    if (string.IsNullOrWhiteSpace(form["email"]) ||
        string.IsNullOrWhiteSpace(form["password"]))
    {
        return Results.BadRequest("The field(s) is empty");
    }
    string email = form["email"].ToString();
    string password = form["password"].ToString();

    Customer? customer = db.Customers.FirstOrDefault(p => p.Email == email && p.Password == password);
    if (customer is null) return Results.Unauthorized();

    var claims = new List<Claim> { new Claim(ClaimTypes.Name, customer.Email) };
    ClaimsIdentity claimsIdentity = new ClaimsIdentity(claims, "Cookies");
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));
    return Results.Redirect("/");
});

app.Run();
