using SharedKernel;

namespace Slices.Retskilder.Contract;

public enum AssessmentBand { None, Weak, Moderate, Strong }

public sealed record RetskilderAssessment(
    CaseId CaseId, AssessmentBand Band, double Score, IReadOnlyList<string> Notes);

public interface IRetskilderService
{
    Result<RetskilderAssessment> Assess(CaseId caseId, IReadOnlyList<ProvisionRef> provisions, string factum);
    int SchemaVersion => 2;   // additive: default interface method
}
