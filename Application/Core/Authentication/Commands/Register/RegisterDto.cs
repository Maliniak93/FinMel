namespace Application.Core.Authentication.Commands.Register;

public class RegisterDto
{
    public RegisterDto(string email, string displayName)
    {
        Email = email;

        DisplayName = displayName;

    }
    public string Email { get; private set; }
    public string DisplayName { get; private set; }
}