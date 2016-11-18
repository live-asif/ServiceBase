﻿using IdentityServer4;
using IdentityServer4.Configuration;
using IdentityServer4.Models;
using IdentityServer4.Services;
using IdentityServer4.Services.InMemory;
using IdentityServer4.Stores;
using IdentityServer4.Stores.InMemory;
using IdentityServer4.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ServiceBase.Config;
using ServiceBase.IdentityServer.Config;
using ServiceBase.IdentityServer.Crypto;
using ServiceBase.IdentityServer.EntityFramework;
using ServiceBase.IdentityServer.Services;
using ServiceBase.Notification.Email;
using ServiceBase.Notification.SMS;
using ServiceBase.Notification.Twilio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;

namespace ServiceBase.IdentityServer.Public
{
    public class Startup
    {
        private readonly ILogger _logger;
        private readonly IHostingEnvironment _environment;
        private readonly IConfigurationRoot _configuration;

        public Startup(
            IHostingEnvironment environment,
            ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<Startup>();
            _configuration = ConfigurationSetup.Configure(environment);
            _environment = environment;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            #region Add application configuration 

            services.AddOptions();
            services.Configure<ApplicationOptions>(_configuration.GetSection("App"));
            services.AddSingleton<IConfiguration>(_configuration);

            #endregion

            #region Add IdentityServer 

            var cert = new X509Certificate2(Path.Combine(
                _environment.ContentRootPath, "idsvr3test.pfx"), "idsrv3test");

            var clients = JsonConvert.DeserializeObject<IEnumerable<Client>>(
                    File.ReadAllText(Path.Combine(_environment.ContentRootPath, "Config", "clients.json")));

            var scopes = JsonConvert.DeserializeObject<IEnumerable<Scope>>(
                   File.ReadAllText(Path.Combine(_environment.ContentRootPath, "Config", "scopes.json"))); 

            var builder = services.AddIdentityServer((options) =>
            {
                //options.RequireSsl = false;     
                options.EventsOptions = new EventsOptions
                {
                    RaiseErrorEvents = true,
                    RaiseFailureEvents = true,
                    RaiseInformationEvents = true,
                    RaiseSuccessEvents = true
                };
                options.UserInteractionOptions.LoginUrl = "/login";
                options.UserInteractionOptions.LogoutUrl = "/logout";
                options.UserInteractionOptions.ConsentUrl = "/consent";
                options.UserInteractionOptions.ErrorUrl = "/error";
                options.AuthenticationOptions.FederatedSignOutPaths.Add("/signout-oidc");
            })
            .AddInMemoryClients(clients)
                .AddInMemoryScopes(scopes)
                .AddTemporarySigningCredential()
                //AddExtensionGrantValidator<Extensions.ExtensionGrantValidator>()
                .AddSecretParser<ClientAssertionSecretParser>()
                .AddSecretValidator<PrivateKeyJwtSecretValidator>()
                .AddSigningCredential(cert);

            services.AddTransient<IProfileService, ProfileService>();
            services.AddTransient<IClientStore, InMemoryClientStore>();
            services.AddTransient<ICorsPolicyService, InMemoryCorsPolicyService>();
            services.AddTransient<IScopeStore, InMemoryScopeStore>();

            #endregion

            #region Add Data Layer 
            
            if (String.IsNullOrWhiteSpace(_configuration["Postgres"]))
            {
                services.AddPostgresStores(_configuration.GetSection("Postgres"));
            }
            else if (String.IsNullOrWhiteSpace(_configuration["Mssql"]))
            {
                services.AddEntityFrameworkStores(_configuration.GetSection("Mssql"));
            }

            #endregion

            #region Add Email Sender 

            services.AddTransient<IEmailService, DebugEmailService>();
            /*services.AddTransient<IEmailService, DefaultEmailService>();
            services.Configure<DefaultEmailServiceOptions>(opt =>
            {
                opt.TemplateDirectoryPath = Path.Combine(_environment.ContentRootPath, "EmailTemplates");
            });

            if (String.IsNullOrWhiteSpace(_configuration["SendGrid"]))
            {
                services.Configure<SendGridOptions>(_configuration.GetSection("SendGrid"));
                services.AddTransient<IEmailSender, SendGridEmailSender>();
            }*/
            // else if o360
            // else if MailGun
            // else if SMTP
            // else default sender 

            // services.AddTransient<IEmailFormatter, EmailFormatter>();

            #endregion

            #region Add SMS Sender 

            if (String.IsNullOrWhiteSpace(_configuration["Twillio"]))
            {
                services.Configure<TwillioOptions>(_configuration.GetSection("Twillio"));
                services.AddTransient<ISmsSender, TwillioSmsSender>();
            }

            #endregion 

            services.AddTransient<ICrypto, DefaultCrypto>();

            services
                .AddMvc()
                .AddRazorOptions(razor =>
                {
                    razor.ViewLocationExpanders.Add(new UI.CustomViewLocationExpander());
                });
        }

        public void Configure(
            IApplicationBuilder app,
            IHostingEnvironment env,
            ILoggerFactory loggerFactory,
            IStoreInitializer storeInitializer)
        {
            /*Func<string, LogLevel, bool> filter = (scope, level) =>
                scope.StartsWith("IdentityServer") ||
                scope.StartsWith("IdentityModel") ||
                level == LogLevel.Error ||
                level == LogLevel.Critical;*/

            loggerFactory.AddConsole();
            loggerFactory.AddDebug();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                //app.UseBrowserLink();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }

            app.UseIdentityServer();

            app.UseCookieAuthentication(new CookieAuthenticationOptions
            {
                AuthenticationScheme = IdentityServerConstants.ExternalCookieAuthenticationScheme,
                AutomaticAuthenticate = false,
                AutomaticChallenge = false
            });

            #region Use third party authentication 

            if (!String.IsNullOrWhiteSpace(_configuration["Authentication:Google:ClientId"]))
            {
                _logger.LogInformation("Registering Google authentication scheme");

                app.UseGoogleAuthentication(new GoogleOptions
                {
                    AuthenticationScheme = "Google",
                    SignInScheme = IdentityServerConstants.ExternalCookieAuthenticationScheme,
                    ClientId = _configuration["Authentication:Google:ClientId"],
                    ClientSecret = _configuration["Authentication:Google:ClientSecret"]
                });
            }


            if (!String.IsNullOrWhiteSpace(_configuration["Authentication:Facebook:AppId"]))
            {
                _logger.LogInformation("Registering Facebook authentication scheme");

                app.UseFacebookAuthentication(new FacebookOptions()
                {
                    AuthenticationScheme = "Facebook",
                    SignInScheme = IdentityServerConstants.ExternalCookieAuthenticationScheme,
                    AppId = _configuration["Authentication:Facebook:AppId"],
                    AppSecret = _configuration["Authentication:Facebook:AppSecret"]
                });
            }


            #endregion

            app.UseStaticFiles();
            app.UseMvcWithDefaultRoute();

            storeInitializer.Initialize();
        }
    }
}
