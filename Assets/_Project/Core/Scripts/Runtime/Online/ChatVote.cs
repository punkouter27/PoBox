using System;
using System.Collections.Generic;

namespace PoBox.Online
{
    /// <summary>
    /// One vote among a fixed set of words, one standing vote a viewer (the last one counts), counted as it comes in
    /// (<see cref="TwitchChat.Poll"/> casts while it <see cref="IsOpen"/>). Nothing is allocated after construction but
    /// the viewer table's growth; reading the tally allocates nothing, and <see cref="Version"/> says when it changed,
    /// so a display redraws only then.
    /// </summary>
    public sealed class ChatVote
    {
        readonly string[] _words;
        readonly int[] _counts;
        readonly Dictionary<string, int> _byViewer = new Dictionary<string, int>(64, StringComparer.Ordinal);

        public ChatVote(string[] words)
        {
            _words = words ?? Array.Empty<string>();
            _counts = new int[_words.Length];
        }

        public bool IsOpen { get; private set; }
        public int Options => _words.Length;
        public int Total { get; private set; }
        /// <summary>Goes up whenever the tally or the open state changes.</summary>
        public int Version { get; private set; }

        public int Count(int option) => option >= 0 && option < _counts.Length ? _counts[option] : 0;

        /// <summary>A fresh vote: every count back to nothing.</summary>
        public void Open()
        {
            Array.Clear(_counts, 0, _counts.Length);
            _byViewer.Clear();
            Total = 0;
            IsOpen = true;
            Version++;
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            Version++;
        }

        /// <summary>The option this word is, or -1.</summary>
        public int IndexOf(string word)
        {
            for (int i = 0; i < _words.Length; i++)
                if (string.Equals(_words[i], word, StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>Counts the viewer's vote if the vote is open and the word is one of its options; true if it was.</summary>
        public bool TryCast(string viewer, string word)
        {
            if (!IsOpen || string.IsNullOrEmpty(viewer)) return false;
            int option = IndexOf(word);
            if (option < 0) return false;
            if (_byViewer.TryGetValue(viewer, out int before))
            {
                if (before == option) return true;
                _counts[before]--;
            }
            else Total++;
            _byViewer[viewer] = option;
            _counts[option]++;
            Version++;
            return true;
        }

        /// <summary>The option with the most votes, if it has at least <paramref name="minVotes"/>; -1 otherwise. A tie goes to the earlier option.</summary>
        public int Winner(int minVotes)
        {
            int best = -1, most = Math.Max(1, minVotes) - 1;
            for (int i = 0; i < _counts.Length; i++)
                if (_counts[i] > most) { most = _counts[i]; best = i; }
            return best;
        }
    }
}
