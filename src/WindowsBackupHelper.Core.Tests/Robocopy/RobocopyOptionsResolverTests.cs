using WindowsBackupHelper.Core.Models;
using WindowsBackupHelper.Core.Robocopy;

namespace WindowsBackupHelper.Core.Tests.Robocopy;

public sealed class RobocopyOptionsResolverTests
{
    private static RobocopyOptionSet EmptySet(string id = "x") => new() { Id = id };

    [Fact]
    public void Resolve_WithOnlyAppDefaults_FallsBackToSafeRetryWaitValues()
    {
        var resolved = RobocopyOptionsResolver.Resolve(EmptySet());

        Assert.Equal(RobocopyOptionsResolver.FallbackRetries, resolved.Retries);
        Assert.Equal(RobocopyOptionsResolver.FallbackWaitSeconds, resolved.WaitSeconds);
        Assert.False(resolved.Mirror);
    }

    [Fact]
    public void Resolve_AppDefaultsRetriesAndWait_AreUsedWhenNoOverrides()
    {
        var appDefaults = EmptySet();
        appDefaults.Retries = 5;
        appDefaults.WaitSeconds = 10;

        var resolved = RobocopyOptionsResolver.Resolve(appDefaults);

        Assert.Equal(5, resolved.Retries);
        Assert.Equal(10, resolved.WaitSeconds);
    }

    [Fact]
    public void Resolve_PairOverride_WinsOverJobAndAppDefault()
    {
        var appDefaults = EmptySet("app");
        appDefaults.Mirror = false;
        appDefaults.Retries = 3;
        appDefaults.WaitSeconds = 5;

        var jobOverrides = EmptySet("job");
        jobOverrides.Mirror = true;

        var pairOverrides = EmptySet("pair");
        pairOverrides.Mirror = false;

        var resolved = RobocopyOptionsResolver.Resolve(appDefaults, jobOverrides, pairOverrides);

        Assert.False(resolved.Mirror); // pair's explicit false beats job's true
    }

    [Fact]
    public void Resolve_JobOverride_WinsOverAppDefault_WhenPairLeavesFieldNull()
    {
        var appDefaults = EmptySet("app");
        appDefaults.Retries = 3;
        appDefaults.WaitSeconds = 5;
        appDefaults.MultithreadCount = 4;

        var jobOverrides = EmptySet("job");
        jobOverrides.MultithreadCount = 16;

        var pairOverrides = EmptySet("pair"); // MultithreadCount left null -> falls through to job

        var resolved = RobocopyOptionsResolver.Resolve(appDefaults, jobOverrides, pairOverrides);

        Assert.Equal(16, resolved.MultithreadCount);
    }

    [Fact]
    public void Resolve_FalseOverride_IsNotConflatedWithUnset()
    {
        // A pair explicitly setting Purge=false must not be treated as "no opinion" and
        // fall through to a job/app default of true.
        var appDefaults = EmptySet("app");
        appDefaults.Retries = 3;
        appDefaults.WaitSeconds = 5;
        appDefaults.Purge = true;

        var pairOverrides = EmptySet("pair");
        pairOverrides.Purge = false;

        var resolved = RobocopyOptionsResolver.Resolve(appDefaults, pairOverrides: pairOverrides);

        Assert.False(resolved.Purge);
    }

    [Fact]
    public void Resolve_ExtraRawArguments_ConcatenatesAcrossAllThreeLevels_InsteadOfOverriding()
    {
        var appDefaults = EmptySet("app");
        appDefaults.Retries = 3;
        appDefaults.WaitSeconds = 5;
        appDefaults.ExtraRawArguments = "/NFL";

        var jobOverrides = EmptySet("job");
        jobOverrides.ExtraRawArguments = "/NDL";

        var pairOverrides = EmptySet("pair");
        pairOverrides.ExtraRawArguments = "/NP";

        var resolved = RobocopyOptionsResolver.Resolve(appDefaults, jobOverrides, pairOverrides);

        Assert.Equal("/NFL /NDL /NP", resolved.ExtraRawArguments);
    }

    [Fact]
    public void Resolve_ExtraRawArguments_SkipsNullOrWhitespaceLevels()
    {
        var appDefaults = EmptySet("app");
        appDefaults.Retries = 3;
        appDefaults.WaitSeconds = 5;
        appDefaults.ExtraRawArguments = "/NFL";

        var resolved = RobocopyOptionsResolver.Resolve(appDefaults, jobOverrides: null, pairOverrides: EmptySet("pair"));

        Assert.Equal("/NFL", resolved.ExtraRawArguments);
    }

    [Fact]
    public void CreateMaterializedOverride_PopulatesCheckboxFields_WithCurrentlyEffectiveValues()
    {
        // Regression test: enabling a job/pair override used to start every field at null,
        // which rendered as a confusing indeterminate checkbox and required un-ticking every
        // other option to isolate the one the user actually wanted to change. A freshly-created
        // override should instead start as an honest, fully-concrete on/off snapshot of what's
        // already in effect.
        var appDefaults = EmptySet("app");
        appDefaults.Retries = 3;
        appDefaults.WaitSeconds = 5;
        appDefaults.CopySubdirectories = true;
        appDefaults.Mirror = false;

        var jobOverrides = EmptySet("job");
        jobOverrides.Mirror = true;

        var materialized = RobocopyOptionsResolver.CreateMaterializedOverride("new-id", appDefaults, jobOverrides);

        Assert.Equal("new-id", materialized.Id);
        Assert.True(materialized.CopySubdirectories); // inherited from app defaults
        Assert.True(materialized.Mirror); // inherited from job override, winning over app default
        Assert.False(materialized.Purge); // untouched anywhere -> resolves to false, not null
    }

    [Fact]
    public void CreateMaterializedOverride_LeavesNumericAndStringFieldsNull()
    {
        // Only checkbox-backed (bool) fields are materialized; text/number fields stay blank
        // ("not set here, inherits") since a blank text box isn't ambiguous the way a tri-state
        // checkbox is. ExtraRawArguments is also left null since the resolver concatenates it
        // additively — copying the already-resolved text down would double it up next resolve.
        var appDefaults = EmptySet("app");
        appDefaults.Retries = 3;
        appDefaults.WaitSeconds = 5;
        appDefaults.MultithreadCount = 8;
        appDefaults.CopyFlags = "DAT";
        appDefaults.ExtraRawArguments = "/NFL";

        var materialized = RobocopyOptionsResolver.CreateMaterializedOverride("new-id", appDefaults);

        Assert.Null(materialized.MultithreadCount);
        Assert.Null(materialized.CopyFlags);
        Assert.Null(materialized.Retries);
        Assert.Null(materialized.WaitSeconds);
        Assert.Null(materialized.ExtraRawArguments);
    }

    [Fact]
    public void BackfillNullBooleans_FillsOnlyStillNullFields_LeavingExplicitValuesUntouched()
    {
        // Regression test: a legacy job/pair RobocopyOptionSet created before materialization
        // existed (or hand-edited in the DB) can have a mix of explicitly-set and still-null
        // boolean fields. Backfilling must resolve only the null ones from the cascade below —
        // an already-explicit false must NOT be overwritten by a true default from below.
        var appDefaults = EmptySet("app");
        appDefaults.Retries = 3;
        appDefaults.WaitSeconds = 5;
        appDefaults.CopySubdirectories = true;
        appDefaults.Purge = true;

        var legacyJobOverride = EmptySet("job");
        legacyJobOverride.Mirror = true; // explicitly set, should survive untouched
        legacyJobOverride.Purge = false; // explicitly set to false, must not be overwritten by app default's true
        // CopySubdirectories left null -> should backfill to the app default (true)

        RobocopyOptionsResolver.BackfillNullBooleans(legacyJobOverride, appDefaults);

        Assert.True(legacyJobOverride.Mirror);
        Assert.False(legacyJobOverride.Purge);
        Assert.True(legacyJobOverride.CopySubdirectories);
    }

    [Fact]
    public void BackfillNullBooleans_DoesNotTouchNumericOrStringFields()
    {
        var appDefaults = EmptySet("app");
        appDefaults.Retries = 3;
        appDefaults.WaitSeconds = 5;
        appDefaults.MultithreadCount = 8;

        var legacyOverride = EmptySet("job");
        RobocopyOptionsResolver.BackfillNullBooleans(legacyOverride, appDefaults);

        Assert.Null(legacyOverride.MultithreadCount);
        Assert.Null(legacyOverride.Retries);
        Assert.Null(legacyOverride.WaitSeconds);
    }
}
