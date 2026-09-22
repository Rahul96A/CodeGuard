using Microsoft.Extensions.Logging;

namespace Banking;

public sealed class OnboardingService(ILogger<OnboardingService> logger)
{
    public void Register(Applicant applicant)
    {
        logger.LogInformation("Registering {Name} with TFN {Tfn} born {Dob}", applicant.FullName, applicant.TaxFileNumber, applicant.DateOfBirth);
        // TODO remove: sample tfn for testing = 123 456 782
    }
}

public sealed record Applicant(string FullName, string TaxFileNumber, DateOnly DateOfBirth);
