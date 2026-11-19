using Xunit;

namespace ClaudeUsageTray.Tests;

/// <summary>
/// 환경 변수가 있어야 의미가 있는 테스트(실물 키를 쓰는 확인 등). 변수가 없으면 통과가 아니라 Skip 으로 보고한다 —
/// xUnit 2 에는 실행 중 건너뛰기가 없어, 단정 없이 return 하면 CI 에서 "통과"로 보였다(#310).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresEnvironmentFactAttribute : FactAttribute
{
    public RequiresEnvironmentFactAttribute(string variable)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
            Skip = $"{variable} 환경 변수가 없어 건너뜀";
    }
}
