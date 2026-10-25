using System.Text;
using Pluto.Agents;

namespace Pluto.Tests;

public class ViewModelTests
{
    private DateTimeOffset _now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private (Pet Pet, PetViewModel Vm) Make(Dock dock = Dock.Notch)
    {
        var pet = new Pet(action => action(), new PetSettings { Dock = dock });
        var todo = new Pluto.Todos.TodoViewModel(new Pluto.Todos.TodoStore(null, () => _now));
        return (pet, new PetViewModel(pet, () => _now, todo));
    }

    private static void Hook(Pet pet, string json) => pet.Receive(Encoding.UTF8.GetBytes($$"""{"agent":"claude","payload":{{json}}}"""));

    [Fact]
    public void It_stays_small_tucked_in_the_notch_or_just_its_face_when_floating()
    {
        var (_, vm) = Make();
        vm.Refresh();
        Assert.Equal(Look.Tucked, vm.Look);
        Assert.Equal(Mood.Happy, vm.Mood); // hello

        vm.DockChoice = 1;
        Assert.Equal(Look.Face, vm.Look);
        vm.NotchAt(400);
        Assert.Equal(Look.Tucked, vm.Look);
        Assert.Equal(400, vm.Settings.NotchX);
    }

    [Fact]
    public void Something_happening_shows_one_note_for_a_moment_with_a_sound_then_it_is_small_again()
    {
        var (pet, vm) = Make();
        var moments = new List<Moment>();
        vm.MomentHappened += moments.Add;

        Hook(pet, """{"hook_event_name":"UserPromptSubmit","session_id":"s","cwd":"C:/code/shop"}""");
        Assert.Equal(Look.Note, vm.Look);
        Assert.Equal("Claude Code is working", vm.NoteTitle);
        Assert.Equal("claude", vm.NoteIcon);

        _now += PetViewModel.NoteFor + TimeSpan.FromSeconds(1);
        vm.Refresh();
        Assert.Equal(Look.Tucked, vm.Look); // working on, but the pet stays small

        Hook(pet, """{"hook_event_name":"Stop","session_id":"s","cwd":"C:/code/shop"}""");
        Assert.Equal(Look.Note, vm.Look);
        Assert.Equal("Claude Code is done", vm.NoteTitle);
        Assert.Equal(Mood.Happy, vm.Mood);
        Assert.Equal([Moment.Started, Moment.Finished], moments);

        _now = _now.AddMinutes(11);
        vm.Refresh();
        Assert.Equal(Mood.Sleepy, vm.Mood);
        Assert.Equal(Look.Tucked, vm.Look);
    }

    [Fact]
    public void Size_eyes_volume_and_sounds_are_settings()
    {
        var (_, vm) = Make();
        vm.SizeChoice = 2;
        vm.EyesChoice = 1;
        vm.Volume = 140;
        Assert.Equal(NotchSize.Large, vm.Settings.Size);
        Assert.Equal(EyeStyle.Oval, vm.Eyes);
        Assert.Equal(100, vm.Settings.Volume);

        var done = vm.Sounds[(int)SoundKind.Finished];
        Assert.Equal(0, done.Choice); // Pluto's own
        done.Choose(1);
        Assert.Equal(string.Empty, vm.Settings.DoneSound);
        Assert.Equal("Silent", done.Detail);
        Assert.NotNull(done.UseFile("C:/music/notes.txt")); // not a sound file
        done.Choose(0);
        Assert.Null(vm.Settings.DoneSound);
    }

    [Fact]
    public void A_click_opens_the_list_leaving_folds_it_and_the_hiveory_section_needs_its_option()
    {
        var (_, vm) = Make();
        vm.ToggleList();
        Assert.Equal(Look.List, vm.Look);
        vm.PointerLeft();
        Assert.Equal(Look.Tucked, vm.Look);

        vm.Section = Section.Hiveory;
        Assert.Equal(Section.All, vm.Section); // the Hiveory option is off
        vm.Section = Section.Todo;
        Assert.True(vm.ShowTodo);
        Assert.False(vm.ShowAgents);
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
        Assert.Equal(NotchSize.Small, s.Size);
        Assert.Equal(EyeStyle.Dots, s.Eyes);
        Assert.Null(s.DoneSound);
        Assert.Null(s.FloatX);
    }
}
