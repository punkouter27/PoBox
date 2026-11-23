namespace PoBox.Audio
{
    /// <summary>
    /// The tournament standard's five voice emotions (shared across the Po games), each heard at an intensity level
    /// 1 (barely) to 5 (all out): the rows of the 5x5 layer of the voice matrix (<see cref="VoiceMatrix"/>).
    /// Each is played as one of the older <see cref="Emotion"/>s when there is no recording of its own
    /// (<see cref="VoiceMatrix.Map"/>).
    /// </summary>
    public enum Emotion5 { Angry, Happy, Hurt, Insult, Celebrate }
}
