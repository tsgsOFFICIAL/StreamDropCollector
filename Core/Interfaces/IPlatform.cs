using Models;

namespace Core.Interfaces
{
    internal interface IPlatform
    {
        string Name { get; }

        Task InitializeAsync();
        Task<bool> LoginAsync(AccountInfo account);
    }
}