namespace Application.Core.Authentication.Commands.Register;

public class RegisterDto
{
    public RegisterDto(string email, string token, string displayName)
    {
        Email = email;
        Token = token;
        DisplayName = displayName;

    }
    public string Email { get; private set; }
    public string Token { get; private set; }
    public string DisplayName { get; private set; }
}
