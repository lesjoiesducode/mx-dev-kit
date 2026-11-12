using System;
using System.Collections.Generic;

namespace MxDevKit;

// The excuses of the Dev excuses bubble, in English and French. Classics of developer folklore,
// in our own words. The French ones are written for French developers, not translated.
public static class DevExcuses
{
    public static readonly IReadOnlyList<string> English = new[]
    {
        "It works on my machine.",
        "It worked yesterday.",
        "That's not a bug, it's a feature.",
        "It must be a caching issue.",
        "It's always DNS.",
        "Someone must have changed my code.",
        "I haven't touched that file in weeks.",
        "But all the tests are green.",
        "It worked perfectly in my head.",
        "My code's compiling.",
        "It's a known bug in the framework.",
        "The third-party API must be down.",
        "It's a timezone issue.",
        "Must be a race condition.",
        "Have you tried clearing your cache?",
        "Have you tried turning it off and on again?",
        "That's an edge case, nobody does that.",
        "That wasn't in the spec.",
        "That was only meant to be temporary.",
        "The previous developer wrote that part.",
        "Don't touch it, it's legacy.",
        "It works in staging.",
        "Did you pull the latest changes?",
        "Did you reinstall node_modules?",
        "Someone must have messed up the merge.",
        "Someone deployed on a Friday.",
        "Git blame says it was me, but I don't remember writing it.",
        "It's a Heisenbug: it disappears when I look at it.",
        "It only fails in production.",
        "That's a front-end problem.",
        "The docs are out of date.",
        "Claude told me it would work.",
        "The AI wrote that part.",
        "We'll fix it in the next sprint.",
        "It's a configuration issue, not a bug.",
        "That test is flaky, just run it again.",
        "Nobody told me about that.",
        "You must have the wrong version.",
        "It must be a character encoding issue.",
        "Probably a cosmic ray flipped a bit.",
        "It's an undocumented feature.",
        "The client changed their mind at the last minute.",
    };

    public static readonly IReadOnlyList<string> French = new[]
    {
        "Ça marche sur mon poste.",
        "Ça marchait hier.",
        "C'est pas un bug, c'est une feature.",
        "C'est un problème de cache.",
        "C'est le DNS, c'est toujours le DNS.",
        "Quelqu'un a touché à mon code.",
        "J'ai pas touché à ce fichier depuis des semaines.",
        "Pourtant, tous les tests passent.",
        "Dans ma tête, ça marchait.",
        "Ça compile.",
        "C'est un bug connu du framework.",
        "L'API du prestataire doit être en rade.",
        "C'est un problème de fuseau horaire.",
        "Ça doit être une race condition.",
        "T'as essayé de vider ton cache ?",
        "T'as essayé d'éteindre et de rallumer ?",
        "C'est un cas limite, personne ne fait ça.",
        "C'était pas dans le cahier des charges.",
        "C'était censé être provisoire.",
        "C'est l'ancien dev qui a écrit ça.",
        "Touche pas, c'est du legacy.",
        "En préprod, ça marche.",
        "T'as bien fait un git pull ?",
        "T'as relancé npm install ?",
        "Quelqu'un a raté son merge.",
        "Quelqu'un a mis en prod un vendredi.",
        "D'après git blame c'est moi, mais je m'en souviens pas.",
        "C'est un Heisenbug, il disparaît dès qu'on le regarde.",
        "Ça plante qu'en prod.",
        "C'est côté front, pas chez moi.",
        "La doc est pas à jour.",
        "Claude m'a dit que ça marchait.",
        "C'est l'IA qui a écrit ce bout-là.",
        "On corrigera au prochain sprint.",
        "C'est un souci de config, pas un bug.",
        "Ce test est capricieux, relance-le.",
        "Personne m'a prévenu.",
        "T'as pas la bonne version.",
        "Encore un problème d'accents.",
        "Sûrement un rayon cosmique qui a inversé un bit.",
        "C'est une fonctionnalité non documentée.",
        "Le client a changé d'avis au dernier moment.",
    };

    public static IReadOnlyList<string> For(Strings strings) => strings.Code == "fr" ? French : English;
}

// Draws excuses at random, never the same one twice in a row.
public sealed class ExcusePicker
{
    private readonly Random _random;
    private readonly object _sync = new();
    private string? _last;

    public ExcusePicker(Random? random = null)
    {
        _random = random ?? new Random();
    }

    public string Next(IReadOnlyList<string> excuses)
    {
        if (excuses.Count == 0)
        {
            return "";
        }

        lock (_sync)
        {
            var last = _last == null ? -1 : IndexOf(excuses, _last);
            int i;
            if (last < 0 || excuses.Count == 1)
            {
                i = _random.Next(excuses.Count);
            }
            else
            {
                // One of the others: draw among Count - 1 and skip the last one.
                i = _random.Next(excuses.Count - 1);
                if (i >= last)
                {
                    i++;
                }
            }

            _last = excuses[i];
            return _last;
        }
    }

    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == value)
            {
                return i;
            }
        }

        return -1;
    }
}
