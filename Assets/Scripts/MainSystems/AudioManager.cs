using UnityEngine;

/// <summary>
/// The one place music and sound effects are played, and the one place GameSettings' volume numbers
/// become audible.
///
/// A PersistantSingleton with no prefab and no scene presence, created on demand by the bootstrap
/// below - the same shape RunManager has, and for the same reason. Game.unity reloads on every level,
/// and the menu is a separate scene; a scene-owned AudioSource restarts its track on each load, which
/// is exactly what the script-less "---AUDIO/AudioManager" object in Game.unity used to do. Surviving
/// the load is the whole point.
///
/// Two AudioSources with their volumes multiplied rather than an AudioMixer. A mixer's exposed
/// parameters can only be created by hand in the Editor - there is no scripted way to expose one - so a
/// generated setup cannot build itself, and for three sliders over two sources the arithmetic here is
/// the entire benefit a mixer would have provided. A mixer remains the upgrade path the moment anything
/// wants an effect on a bus.
/// </summary>
public class AudioManager : PersistantSingleton<AudioManager>
{
    private AudioSource musicSource;

    private AudioSource sfxSource;

    /// What is playing, tracked so PlayMusic can be called freely on every scene load without
    /// restarting the track that is already running.
    private AudioClip currentMusic;

    /// <summary>
    /// Creates the manager before the first scene loads, so the main menu has music without needing an
    /// AudioSource authored into it - it has none today - and so nothing has to remember to place this
    /// in a scene.
    ///
    /// BeforeSceneLoad rather than AfterSceneLoad: a MainMenu or BattleManager Awake that asks for
    /// Instance should find it already there rather than racing it.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) { return; }

        GameObject host = new(nameof(AudioManager));

        host.AddComponent<AudioManager>();

        // Applied here rather than in Awake because it is not an audio concern at all - this is simply
        // the one hook that already runs once, before anything is shown, on every entry to the game.
        DisplaySettings.Apply();
    }

    protected override void Awake()
    {
        base.Awake();

        // base.Awake destroys this object when another instance already holds the slot, so a second
        // one must not carry on and build sources onto a corpse.
        if (Instance != this) { return; }

        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.loop = true;
        musicSource.playOnAwake = false;

        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.loop = false;
        sfxSource.playOnAwake = false;

        ApplyVolumes();
    }

    /// <summary>
    /// Pushes the current GameSettings volumes onto both sources.
    ///
    /// Called by whoever changed a setting rather than polled, and read from GameSettings rather than
    /// passed in, so there is exactly one answer to "how loud is music" and it is the saved value.
    /// </summary>
    public void ApplyVolumes()
    {
        if (musicSource != null) { musicSource.volume = GameSettings.MasterVolume * GameSettings.MusicVolume; }

        if (sfxSource != null) { sfxSource.volume = GameSettings.MasterVolume * GameSettings.SfxVolume; }
    }

    /// <summary>
    /// Starts a looping track, or does nothing if that track is already the one playing.
    ///
    /// The no-op case is what makes this safe to call from every scene's Start: a level reload asking
    /// for the battle theme it is already playing must not restart it from the top.
    /// </summary>
    public void PlayMusic(AudioClip clip)
    {
        if (musicSource == null) { return; }

        if (clip == null)
        {
            musicSource.Stop();
            currentMusic = null;
            return;
        }

        if (currentMusic == clip && musicSource.isPlaying) { return; }

        currentMusic = clip;
        musicSource.clip = clip;
        musicSource.Play();
    }

    public void StopMusic()
    {
        PlayMusic(null);
    }

    /// <summary>
    /// Fires a one-shot effect. PlayOneShot rather than clip-plus-Play so overlapping effects layer
    /// instead of cutting each other off.
    /// </summary>
    public void PlaySfx(AudioClip clip)
    {
        if (sfxSource == null || clip == null) { return; }

        sfxSource.PlayOneShot(clip);
    }
}
