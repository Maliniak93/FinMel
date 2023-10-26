using Domain.Entities.Identity;

namespace Application.Abstractions.Authentication;
public interface IToken
{
    string CreateToken(AppUser user);
}
