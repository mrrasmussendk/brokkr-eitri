using SharedKernel;

namespace Slices.Domme.Contract;

public enum AssessmentBand { None, Weak, Moderate, Strong }

public sealed record DommeAssessment(
    CaseId CaseId, AssessmentBand Band, double Score, IReadOnlyList<string> Notes);

public interface IDommeService
{
    Result<DommeAssessment> Assess(CaseId caseId, IReadOnlyList<ProvisionRef> provisions, string factum);
}
