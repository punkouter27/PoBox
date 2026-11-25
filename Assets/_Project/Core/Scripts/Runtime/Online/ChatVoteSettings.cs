using System;
using UnityEngine;

namespace PoBox.Online
{
    /// <summary>
    /// The tunables of chat's corner (<see cref="ChatCorner"/>): the words Twitch chat votes with between rounds, what
    /// each one does to the boxer it is for in the next round, and when the vote opens and closes.
    ///
    /// What an instruction can do is what the game can do to a trained brain from outside it: the shipped policies have
    /// no input for tactics, so "!body" cannot make one throw to the body. It works through the drives instead
    /// (MjBrain hands the fighter's drive strength to MjBoxer.DriveScale each physics step): how much of the daze and
    /// hurt weakening is fought through (<see cref="Option.hurtOverride"/>), a cap on the drives
    /// (<see cref="Option.driveScale"/>), and energy spent or saved (<see cref="Option.reservePerSecond"/>, on boxers
    /// that tire). The line the broadcast says is the instruction as chat gave it.
    /// </summary>
    [CreateAssetMenu(menuName = "PoBox/Chat Vote Settings", fileName = "ChatVoteSettings")]
    public class ChatVoteSettings : ScriptableObject
    {
        [Serializable]
        public class Option
        {
            [Tooltip("The chat word, lower case, without the '!'.")]
            public string word = "";
            [Tooltip("Shown in the HUD's tally.")]
            public string label = "";
            [Tooltip("Share of the daze and hurt weakening fought through: 0 none, 1 the drives as if unhurt.")]
            [Range(0f, 1f)] public float hurtOverride;
            [Tooltip("Cap on the drives for the round, as a share of what they would be (1 leaves them alone).")]
            [Range(0.5f, 1f)] public float driveScale = 1f;
            [Tooltip("Muscle reserve added each second of the round (negative spends it), on boxers that tire.")]
            [Range(-0.05f, 0.05f)] public float reservePerSecond;
            [Tooltip("What the broadcast says when the vote goes this way: \"{0}\" is the boxer's name.")]
            public string line = "";
        }

        public enum Target
        {
            /// <summary>The corner chat is cheering (!red / !blue, or a name); on a tie, the one behind on punches landed.</summary>
            ChatFavourite,
            /// <summary>The one who landed fewer this bout: chat helps the underdog.</summary>
            Underdog,
            Red,
            Blue,
        }

        [Tooltip("The instructions, in the order shown. On a tie the earlier one wins.")]
        public Option[] options =
        {
            new Option { word = "jab", label = "JAB", hurtOverride = 0.15f, driveScale = 1f, reservePerSecond = 0.003f, line = "{0}, work behind the jab!" },
            new Option { word = "body", label = "BODY", hurtOverride = 0.25f, driveScale = 1f, reservePerSecond = -0.003f, line = "{0}, go downstairs, dig to the body!" },
            new Option { word = "defend", label = "DEFEND", hurtOverride = 0f, driveScale = 0.92f, reservePerSecond = 0.01f, line = "{0}, hands up, survive the round!" },
            new Option { word = "pressure", label = "PRESSURE", hurtOverride = 0.5f, driveScale = 1f, reservePerSecond = -0.008f, line = "{0}, walk him down, pressure!" },
        };

        [Tooltip("Whose corner chat is.")]
        public Target target = Target.ChatFavourite;
        [Tooltip("Seconds into the break the vote opens (after the bell's line).")]
        public float openAfter = 0.5f;
        [Tooltip("Seconds before the break ends the vote closes and the winner is said.")]
        public float closeBefore = 1.5f;
        [Tooltip("Fewest votes that count; fewer and the boxer boxes as it would.")]
        public int minVotes = 1;
        [Tooltip("Name of the HUD's UI Toolkit Label that shows the vote (hidden with the 'hidden' class).")]
        public string hudLabel = "chat-vote";
    }
}
