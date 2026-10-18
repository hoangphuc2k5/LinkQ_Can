using Weighing.Local.Api.Sync;

namespace Weighing.Tests;

public class UnitTests
{
    [Theory]
    [InlineData(0, 0)] [InlineData(1, 2)] [InlineData(2, 4)] [InlineData(3, 8)] [InlineData(5, 32)]
    [InlineData(6, 60)] [InlineData(50, 60)] // bị chặn ở max
    public void Backoff_is_exponential_and_capped(int failures, int expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), Backoff.Compute(failures, 2, 60));

    [Theory]
    [InlineData("  51a  123.45 ", "51A 123.45")]
    [InlineData("abc", "ABC")]
    [InlineData("", "")]
    public void Plate_is_normalized(string input, string expected) => Assert.Equal(expected, PlateNormalizer.Normalize(input));

    private static WeighingRecordDto Rec(string plate = "51A-1", decimal kg = 100m, string client = "Machine A", DateTime? at = null) =>
        new(Guid.NewGuid(), plate, kg, Direction.In, CargoStatus.Loaded, at ?? DateTime.UtcNow, client);

    [Fact] public void Valid_record_passes() => Assert.Null(WeighingValidator.Validate(Rec(), DateTime.UtcNow));
    [Fact] public void Zero_weight_fails() => Assert.NotNull(WeighingValidator.Validate(Rec(kg: 0m), DateTime.UtcNow));
    [Fact] public void Negative_weight_fails() => Assert.NotNull(WeighingValidator.Validate(Rec(kg: -5m), DateTime.UtcNow));
    [Fact] public void Lowercase_plate_fails() => Assert.NotNull(WeighingValidator.Validate(Rec(plate: "abc"), DateTime.UtcNow));
    [Fact] public void Long_plate_fails() => Assert.NotNull(WeighingValidator.Validate(Rec(plate: new string('A', 21)), DateTime.UtcNow));
    [Fact] public void Future_time_fails() => Assert.NotNull(WeighingValidator.Validate(Rec(at: DateTime.UtcNow.AddDays(3)), DateTime.UtcNow));
    [Fact] public void Four_decimals_fail() => Assert.NotNull(WeighingValidator.Validate(Rec(kg: 1.2345m), DateTime.UtcNow));

    [Fact]
    public void Tracker_counts_failures_and_resets_on_success()
    {
        var t = new SyncStatusTracker();
        t.SetOffline("x"); t.SetFailure("y");
        Assert.Equal(2, t.ConsecutiveFailures);
        t.SetSuccess();
        Assert.Equal(0, t.ConsecutiveFailures);
        Assert.True(t.Snapshot().Online);
    }
}
