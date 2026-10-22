using System.Text;
using HiveoryPet.Agents;

namespace HiveoryPet.Tests;

public class ViewModelTests
{
    private DateTimeOffset _now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private (Pet Pet, PetViewModel Vm) Make(Dock dock = Dock.Notch)
    {
        var pet = new Pet(action => action(), new PetSettings { Dock = dock });
        return (pet, new PetViewModel(pet, () => _now));
    }

    private static void Hook(Pet pet, string json) => pet.Receive(Encoding.UTF8.GetBytes($$"""{"agent":"claude","payload":{{json}}}"""));

    [Fact]
    public void It_says_hello_then_tucks_away_or_shows_its_face_when_floating()
    {
        var (_, vm) = Make();
        vm.Refresh();
        Assert.Equal(Look.Compact, vm.Look);
        Assert.Equal(Mood.Happy, vm.Mood);

        _now = _now.AddSeconds(5);
        vm.Refresh();
        Assert.Equal(Look.Tucked, vm.Look);
        Assert.Equal("Nothing running", vm.Title);

        vm.DockChoice = 1;
        Assert.Equal(Dock.Floating, vm.Dock);
        Assert.Equal(Look.Face, vm.Look);

        vm.NotchAt(400);
        Assert.Equal(Look.Tucked, vm.Look);
        Assert.Equal(400, vm.Settings.NotchX);
    }

    [Fact]
    public void Work_shows_the_pill_a_click_the_list_and_leaving_folds_it()
    {
        var (pet, vm) = Make();
        _now = _now.AddSeconds(5);
        Hook(pet, """{"hook_event_name":"UserPromptSubmit","session_id":"s","cwd":"C:/code/shop"}""");
        Assert.Equal(Look.Compact, vm.Look);
        Assert.Equal(Mood.Working, vm.Mood);
        Assert.Equal("Claude Code is working", vm.Title);

        vm.ToggleList();
        Assert.Equal(Look.List, vm.Look);
        Assert.Single(vm.Rows);

        vm.SetHovered(false);
        Assert.Equal(Look.Compact, vm.Look);
    }

    [Fact]
    public void A_finished_turn_is_a_happy_moment_and_long_quiet_makes_it_sleepy()
    {
        var (pet, vm) = Make();
        var moments = new List<Moment>();
        vm.MomentHappened += moments.Add;
        Hook(pet, """{"hook_event_name":"UserPromptSubmit","session_id":"s"}""");
        _now = _now.AddSeconds(30);
        Hook(pet, """{"hook_event_name":"Stop","session_id":"s"}""");
        Assert.Equal([Moment.Finished], moments);
        Assert.Equal(Mood.Happy, vm.Mood);
        Assert.Equal("Claude Code is done", vm.Title);
        Assert.StartsWith("Done in", vm.Subtitle); // the pet times turns with the real clock

        _now = _now.AddMinutes(11);
        vm.Refresh();
        Assert.Equal(Mood.Sleepy, vm.Mood);
        Assert.Equal(Look.Tucked, vm.Look);
    }
}

public class SettingsTests
{
    [Fact]
    public void Settings_missing_from_the_file_keep_their_defaults()
    {
        // An old file (with the six spots) still loads: unknown keys are ignored.
        var s = System.Text.Json.JsonSerializer.Deserialize("""{"Spot":"BottomLeft","Hiveory":true}""", SettingsJson.Default.PetSettings)!;
        Assert.Equal(Dock.Notch, s.Dock);
        Assert.True(s.Sounds);
        Assert.True(s.Sounds);
        Assert.Null(s.FloatX);
    }
}
