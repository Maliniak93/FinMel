namespace Application.Core.Authentication.Commands.Login;
public class LoginDto
{
    public LoginDto(string email, string token, string displayName)
    {
        Email = email;
        Token = token;
        DisplayName = displayName;
    }
    public string Email { get; private set; }
    public string Token { get; private set; }
    public string DisplayName { get; private set; }
}
