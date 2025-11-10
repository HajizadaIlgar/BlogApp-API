using BlogApp.BusinnesLayer.Services.Interfaces;
namespace BlogApp.BusinnesLayer.Services.Implements
{
    public class EmailService : IEmailService
    {
        public async Task SendPasswordResetEmail(string email, string resetLink)
        {
            var message = new MimeKit.MimeMessage();
            message.From.Add(new MimeKit.MailboxAddress("Casino Of Zaur", "Casino@commonity.com"));
            message.To.Add(MimeKit.MailboxAddress.Parse(email));
            message.Subject = "Şifrə sıfırlama linki";
            message.Body = new MimeKit.TextPart("plain")
            {
                Text = $"Şifrəni sıfırlamaq üçün link: {resetLink}"
            };

            using var client = new MailKit.Net.Smtp.SmtpClient();
            await client.ConnectAsync("smtp.gmail.com", 587, MailKit.Security.SecureSocketOptions.StartTls);
            await client.AuthenticateAsync("ilgarh-ab108@code.edu.az", "powmllrpxyaxxjyp");
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

        }

        public Task AccountVerify(string token)
        {
            throw new NotImplementedException();
        }

        public Task SendEmailAsync()
        {
            throw new NotImplementedException();
        }
    }

}
