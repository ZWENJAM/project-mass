using ProjectMass.Models;

namespace ProjectMass.Services;

public interface IProfileService
{
    Task<UserProfile> GetAsync();
    Task SaveAsync(UserProfile profile);
    Task<decimal?> GetEffectiveWeightAsync(UserProfile profile);
    Task<NutritionTargets> SuggestTargetsAsync(UserProfile profile);
}
