using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace TestApp.EventHubApp.WebAPI.Helpers
{
    public interface IEmailService
    {
        Task SendAlertEmailAsync(string subject, string body, string toEmail);
    }

    public class EmailService : IEmailService
    {
        private readonly EmailSettings _emailSettings;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IOptions<EmailSettings> emailSettings, ILogger<EmailService> logger)
        {
            _emailSettings = emailSettings.Value;
            _logger = logger;
        }

        public async Task SendAlertEmailAsync(string subject, string body, string toEmail)
        {
            try
            {
                if (string.IsNullOrEmpty(_emailSettings.SmtpServer))
                {
                    _logger.LogWarning("SMTP server not configured. Email alert simulated: {Subject}", subject);
                    return;
                }

                using var smtpClient = new SmtpClient(_emailSettings.SmtpServer, _emailSettings.SmtpPort)
                {
                    Credentials = new NetworkCredential(_emailSettings.Username, _emailSettings.Password),
                    EnableSsl = _emailSettings.EnableSsl
                };

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(_emailSettings.FromEmail),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = false
                };

                mailMessage.To.Add(toEmail);

                await smtpClient.SendMailAsync(mailMessage);
                _logger.LogInformation("Alert email sent successfully to {Email} with subject: {Subject}", toEmail, subject);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send alert email to {Email}", toEmail);
                throw;
            }
        }
    }

    public class MockEmailService : IEmailService
    {
        private readonly ILogger<MockEmailService> _logger;

        public MockEmailService(ILogger<MockEmailService> logger)
        {
            _logger = logger;
        }

        public Task SendAlertEmailAsync(string subject, string body, string toEmail)
        {
            _logger.LogWarning("MOCK EMAIL ALERT: To={Email}, Subject={Subject}, Body={Body}", 
                toEmail, subject, body);
            return Task.CompletedTask;
        }
    }
}
