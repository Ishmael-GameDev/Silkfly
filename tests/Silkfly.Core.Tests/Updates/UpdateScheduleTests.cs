using Silkfly.Core.Settings;
using Silkfly.Core.Updates;

namespace Silkfly.Core.Tests.Updates;

public sealed class UpdateScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void First_check_is_due() => UpdateSchedule.IsDue(null, Now).ShouldBeTrue();

    [Fact]
    public void Recent_check_is_not_repeated() => UpdateSchedule.IsDue(Now.AddHours(-5), Now).ShouldBeFalse();

    [Fact]
    public void Check_after_six_hours_is_due() => UpdateSchedule.IsDue(Now.AddHours(-6), Now).ShouldBeTrue();

    [Fact]
    public void Timestamp_in_the_future_does_not_block_checks() => UpdateSchedule.IsDue(Now.AddDays(3), Now).ShouldBeTrue();

    [Theory]
    [InlineData("beta", "beta")]
    [InlineData("stable", "stable")]
    [InlineData("nightly", "stable")]
    [InlineData("", "stable")]
    public void Unknown_channels_fall_back_to_stable(string channel, string expected) =>
        new AppSettings { UpdateChannel = channel }.Normalized().UpdateChannel.ShouldBe(expected);
}
