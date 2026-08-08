using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Handles StepGame background music, milestone SFX and global mute/unmute.
/// </summary>
public class StairGameAudioManager : MonoBehaviour
{
    [Header("Audio Sources")]
    [SerializeField] private AudioSource backgroundMusicSource;
    [SerializeField] private AudioSource milestoneSfxSource;

    [Header("Clips")]
    [SerializeField] private AudioClip backgroundMusicClip;
    [SerializeField] private AudioClip milestoneSuccessClip;

    [Header("Volumes")]
    [SerializeField, Range(0f, 1f)] private float musicVolume = 0.25f;
    [SerializeField, Range(0f, 1f)] private float milestoneVolume = 0.75f;

    [Header("Mute Button")]
    [SerializeField] private Button muteButton;
    [SerializeField] private GameObject soundOnIcon;
    [SerializeField] private GameObject soundOffIcon;

    [Header("Startup")]
    [SerializeField] private bool playMusicOnStart = true;
    [SerializeField] private bool startMuted = false;

    public bool IsMuted { get; private set; }

    private void Awake()
    {
        if (muteButton != null)
            muteButton.onClick.AddListener(ToggleMute);
    }

    private void Start()
    {
        ConfigureSources();
        SetMuted(startMuted);

        if (playMusicOnStart)
            PlayBackgroundMusic();
    }

    private void ConfigureSources()
    {
        if (backgroundMusicSource != null)
        {
            backgroundMusicSource.playOnAwake = false;
            backgroundMusicSource.loop = true;
            backgroundMusicSource.volume = musicVolume;

            if (backgroundMusicClip != null)
                backgroundMusicSource.clip = backgroundMusicClip;
        }

        if (milestoneSfxSource != null)
        {
            milestoneSfxSource.playOnAwake = false;
            milestoneSfxSource.loop = false;
            milestoneSfxSource.volume = milestoneVolume;
        }
    }

    public void PlayBackgroundMusic()
    {
        if (backgroundMusicSource == null)
            return;

        if (backgroundMusicSource.clip == null &&
            backgroundMusicClip != null)
        {
            backgroundMusicSource.clip = backgroundMusicClip;
        }

        if (!backgroundMusicSource.isPlaying)
            backgroundMusicSource.Play();
    }

    public void StopBackgroundMusic()
    {
        if (backgroundMusicSource != null)
            backgroundMusicSource.Stop();
    }

    public void PlayMilestoneSound()
    {
        if (milestoneSfxSource == null ||
            milestoneSuccessClip == null)
        {
            return;
        }

        milestoneSfxSource.PlayOneShot(
            milestoneSuccessClip,
            milestoneVolume
        );
    }

    public void ToggleMute()
    {
        SetMuted(!IsMuted);
    }

    public void SetMuted(bool muted)
    {
        IsMuted = muted;

        // Global Unity audio mute: music + milestone + future game sounds.
        AudioListener.volume = muted ? 0f : 1f;

        if (soundOnIcon != null)
            soundOnIcon.SetActive(!muted);

        if (soundOffIcon != null)
            soundOffIcon.SetActive(muted);
    }

    private void OnDestroy()
    {
        // Avoid leaving global audio muted after leaving this scene.
        AudioListener.volume = 1f;
    }
}