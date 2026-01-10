
/// <summary>
/// Represents a handle to a sound emitter that allows validation and tracking.
/// Used to ensure that operations on sound emitters are still valid even if the emitter has been recycled.
/// </summary>

public class SoundHandle
{
    public SoundEmitter Emitter;    // SoundEmitter that is used to play the SoundAudioObjects
    public int Seq;

    /// <summary>
    /// Checks if this handle still references a valid sound emitter.
    /// An emitter may become invalid if it has finished playing and been reallocated.
    /// </summary>
    /// <returns>True if the handle is still valid, false otherwise</returns>
    public bool IsValid()
    {
        return Emitter != null && Emitter.SeqId == Seq; // Are we still attached to the same SoundEmitter ?
    }

    /// <summary>
    /// Initializes a new empty SoundHandle.
    /// </summary>
    public SoundHandle()
    {
        Emitter = null;
    }

    /// <summary>
    /// Initializes a new SoundHandle with the specified sound emitter.
    /// </summary>
    /// <param name="soundEmitter">The sound emitter to create a handle for</param>
    public SoundHandle(SoundEmitter soundEmitter)
    {
        Emitter = soundEmitter;
        Seq = soundEmitter != null ? soundEmitter.SeqId : -1;
    }
}
