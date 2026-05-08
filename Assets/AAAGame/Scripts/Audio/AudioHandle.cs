public readonly struct AudioHandle
{
    public readonly int Id;
    internal AudioHandle(int id) { Id = id; }
    public static AudioHandle Invalid => new AudioHandle(0);
    public bool IsValid => Id != 0;
}
