using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using System.Security.Cryptography.X509Certificates;
using TestOIDCBlazorWASM.API;
using TestOIDCBlazorWASM.Work;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Il est n�cessaire d'avoir deux serveurs d'API s�par�s si on veut ne pas �tre oblig�s d'�tre en MutualTLS,
// car c'est activ� au niveau de l'entr�e du serveur, comme expliqu� sur https://docs.microsoft.com/en-us/aspnet/core/security/authentication/certauth?view=aspnetcore-6.0
// Toutefois, l'avantage est qu'on n'aura pas � supporter CORS, puisque ce sera un user-agent simple
// et pas un navigateur qui passera par cette exposition (et l'autre est sur le m�me h�te).

// Ajout pour g�rer le format JSONPatch, pas encore pris compl�tement en compte en natif dans System.Text.Json
builder.Services.AddControllers(options =>
{
    options.InputFormatters.Insert(0, CustomJPIF.GetJsonPatchInputFormatter());
}).AddNewtonsoftJson();

//builder.Services.AddTransient<CertificateValidation>();
builder.Services.AddAuthentication(CertificateAuthenticationDefaults.AuthenticationScheme).AddCertificate(options =>
{
    options.AllowedCertificateTypes = CertificateTypes.All;
    //options.AllowedCertificateTypes = CertificateTypes.SelfSigned;
    options.Events = new CertificateAuthenticationEvents
    {
        OnCertificateValidated = context =>
        {
            string empreinteReference = builder.Configuration.GetSection("Securite")["EmpreinteCertificatClient"];
            string empreinteRecue = context.ClientCertificate.Thumbprint;
            if (string.Compare(empreinteRecue, empreinteReference) == 0)
                context.Success();
            else
                context.Fail("Invalid certificate");
            return Task.CompletedTask;
        },
        OnAuthenticationFailed = context =>
        {
            context.Fail("Invalid certificate made authentication fail");
            return Task.CompletedTask;
        }
    };
});

// Si �a ne marche pas derri�re un ingress K8S nginx, il faudra jeter un oeil � https://docs.microsoft.com/en-us/aspnet/core/security/authentication/certauth?view=aspnetcore-6.0
//builder.Services.Configure<KestrelServerOptions>(options =>
//{
//    options.ConfigureHttpsDefaults(options =>
//        options.ClientCertificateMode = ClientCertificateMode.RequireCertificate);
//});

// Mode fonctionnant OK pour le client navigateur, mais pas avec Postman
// La transformation du PFX en PEM change tout de m�me le message d'erreur de Postman de "Unable to verify the first certificate" � "socket hang up"
builder.Services.Configure<KestrelServerOptions>(options =>
{
    var cert = new X509Certificate2(
        builder.Configuration.GetSection("Securite")["CheminFichierCertificatClient"],
        builder.Configuration.GetSection("Securite")["MotDePasseCertificatClient"]);
    options.ConfigureHttpsDefaults(o =>
    {
        o.ServerCertificate = cert;
        o.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseHttpsRedirection();

app.UseAuthentication();

// C�t� autorisations, on ne fait pas dans le d�tail sur cette exposition d'API : si le client
// a le bon certificat, il a droit � tous les acc�s
app.UseAuthorization();

app.MapControllers();

app.Run();
