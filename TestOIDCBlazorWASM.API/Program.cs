using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using TestOIDCBlazorWASM.API;
using TestOIDCBlazorWASM.Work;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// On est oblig� d'avoir deux serveurs d'API s�par�s si on veut ne pas �tre oblig�s d'�tre en MutualTLS,
// car c'est activ� au niveau de l'entr�e du serveur, comme expliqu� sur https://docs.microsoft.com/en-us/aspnet/core/security/authentication/certauth?view=aspnetcore-6.0
// Toutefois, l'avantage est qu'on n'aura pas � supporter CORS, puisque ce sera un user-agent simple
// et pas un navigateur qui passera par cette exposition (et l'autre est sur le m�me h�te).

// Ajout pour g�rer le format JSONPatch, pas encore pris compl�tement en compte en natif dans System.Text.Json
builder.Services.AddControllers().AddNewtonsoftJson();
builder.Services.AddControllersWithViews(options =>
{
    options.InputFormatters.Insert(0, CustomJPIF.GetJsonPatchInputFormatter());
});

builder.Services.AddTransient<CertificateValidation>();
builder.Services.AddAuthentication(CertificateAuthenticationDefaults.AuthenticationScheme).AddCertificate(options =>
{
    options.AllowedCertificateTypes = CertificateTypes.SelfSigned;
    options.Events = new CertificateAuthenticationEvents
    {
        OnCertificateValidated = context =>
        {
            var validationService = context.HttpContext.RequestServices.GetService<CertificateValidation>();
            if (validationService.ValidateCertificate(context.ClientCertificate))
            {
                context.Success();
            }
            else
            {
                context.Fail("Invalid certificate");
            }
            return Task.CompletedTask;
        },
        OnAuthenticationFailed = context =>
        {
            context.Fail("Invalid certificate");
            return Task.CompletedTask;
        }
    };
});

// Si �a ne marche pas derri�re un ingress K8S nginx, il faudra jeter un oeil � https://docs.microsoft.com/en-us/aspnet/core/security/authentication/certauth?view=aspnetcore-6.0
builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.ConfigureHttpsDefaults(options =>
        options.ClientCertificateMode = ClientCertificateMode.RequireCertificate);
});

var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseHttpsRedirection();

// C�t� autorisations, on ne fait pas dans le d�tail sur cette exposition d'API : si le client
// a le bon certificat, il a droit � tous les acc�s
app.UseAuthorization();

app.MapControllers();

app.Run();
