using System;

namespace MxDevKit;

// Every text the plugin shows, in English and French.
public sealed class Strings
{
    public static readonly Strings English = new()
    {
        Code = "en",
        GroupProject = "Current project",
        GroupGitHub = "GitHub",
        GroupFocus = "Focus",

        ProjectName = "Project",
        ProjectDescription = "Name and branch of the current project (last folder used in the terminal or the Finder). Click: opens the GitHub repository, or the folder.",
        IssuesName = "Issues",
        IssuesDescription = "Open issues of the current project on GitHub. Click: opens the list.",
        PullRequestsName = "Pull requests",
        PullRequestsDescription = "Open pull requests of the current project on GitHub. Click: opens the list.",
        CiName = "CI status",
        CiDescription = "Latest GitHub Actions run of the current branch. Vibrates when a running workflow finishes. Click: opens the run.",
        AuditName = "Vulnerabilities",
        AuditDescription = "Known vulnerabilities in the dependencies of the current project, at or above the configured severity (high by default). osv-scanner when it is installed (every lockfile: npm, Python, PHP, Go, Rust, .NET...), npm audit otherwise. Click: scans again (never fixes anything).",
        CommitName = "Commit all changes",
        CommitDescription = "Changed files in the current project. Click: stages everything (git add -A) and commits, with a message describing the changes.",
        PushName = "Push",
        PushDescription = "Commits waiting to be pushed. Click: git push (never forced).",
        PullName = "Pull",
        PullDescription = "Commits waiting to be pulled (git fetch runs in the background). Click: git pull --ff-only.",
        AgentName = "Agent in project",
        AgentDescription = "Opens a terminal in the current project and starts the command set in config.json (agent.command, claude by default), for any coding agent or tool.",
        CodingAgentDescription = "Opens a terminal in the current project and starts {0} ({1}). Grey with Install when {1} is not found on this computer: click to see how to install it.",
        ReviewsName = "Review requests",
        ReviewsDescription = "Pull requests waiting for your review, across GitHub. Vibrates when a new one arrives. Click: opens them.",
        InboxName = "GitHub inbox",
        InboxDescription = "Last GitHub event for you: review requested, mention, assignment, your pull request approved, merged, with changes requested or a red CI. Vibrates and notifies. Click: opens it.",
        FocusName = "Focus timer",
        FocusDescription = "Focus timer with a break. Click to start or stop. Vibrates at the end of the session and of the break.",

        ShortIssues = "Issues",
        ShortPullRequests = "PRs",
        ShortCi = "CI",
        ShortAudit = "Vulnerabilities",
        ShortCommit = "Commit",
        ShortPush = "Push",
        ShortPull = "Pull",
        ShortReviews = "Reviews",
        ShortInbox = "GitHub",
        ShortFocus = "Focus",
        ShortBreak = "Break",

        Ok = "OK",
        Failed = "Failed",
        Running = "Running",
        Install = "Install",
        NotInstalled = "not installed",
        LabelSeparator = ": ",
        Minutes = "min",

        EventActionSucceeded = "Git action succeeded",
        EventActionFailed = "Git action failed",
        EventCiPassed = "CI passed",
        EventCiFailed = "CI failed",
        EventReviewRequested = "Review requested",
        EventFocusEnded = "Focus session over",
        EventBreakEnded = "Break over",
        EventMentioned = "Mentioned on GitHub",
        EventAssigned = "Assigned on GitHub",
        EventPrApproved = "Pull request approved",
        EventPrMerged = "Pull request merged",
        EventChangesRequested = "Changes requested",
        EventPrCiFailed = "CI failed on your pull request",

        InboxReviewRequested = "Review requested",
        InboxMention = "You were mentioned",
        InboxTeamMention = "Your team was mentioned",
        InboxAssigned = "Assigned to you",
        InboxApproved = "Approved by {0}",
        InboxChangesRequested = "Changes requested by {0}",
        InboxMerged = "Pull request merged",
        InboxCiFailed = "CI failed on your pull request",
        InboxMore = "{0} more GitHub events",

        NotificationTest = "Notifications work. A click opens your GitHub notifications.",
        NoProject = "No current project yet. Open a terminal in a repository, or a Finder window on macOS, then click again.",
    };

    public static readonly Strings French = new()
    {
        Code = "fr",
        GroupProject = "Projet courant",
        GroupGitHub = "GitHub",
        GroupFocus = "Focus",

        ProjectName = "Projet",
        ProjectDescription = "Nom et branche du projet courant (dernier dossier utilisé dans le terminal ou le Finder). Clic : ouvre le dépôt GitHub, sinon le dossier.",
        IssuesName = "Issues du projet",
        IssuesDescription = "Issues ouvertes du projet courant sur GitHub. Clic : ouvre la liste.",
        PullRequestsName = "PR du projet",
        PullRequestsDescription = "Pull requests ouvertes du projet courant sur GitHub. Clic : ouvre la liste.",
        CiName = "CI du projet",
        CiDescription = "Dernier run GitHub Actions de la branche courante. Vibre quand un workflow en cours se termine. Clic : ouvre le run.",
        AuditName = "Failles du projet",
        AuditDescription = "Failles connues des dépendances du projet courant, à partir de la gravité choisie (high par défaut). osv-scanner s'il est installé (tous les fichiers de verrouillage : npm, Python, PHP, Go, Rust, .NET...), npm audit sinon. Clic : relance l'analyse (ne corrige jamais rien).",
        CommitName = "Commit du projet",
        CommitDescription = "Fichiers modifiés du projet courant. Clic : git add -A puis commit, avec un message qui décrit les modifications.",
        PushName = "Push du projet",
        PushDescription = "Commits à pousser. Clic : git push (jamais forcé).",
        PullName = "Pull du projet",
        PullDescription = "Commits à récupérer (git fetch tourne en arrière-plan). Clic : git pull --ff-only.",
        AgentName = "Agent dans le projet",
        AgentDescription = "Ouvre un terminal dans le projet courant et y lance la commande choisie dans config.json (agent.command, claude par défaut), pour n'importe quel agent ou outil.",
        CodingAgentDescription = "Ouvre un terminal dans le projet courant et y lance {0} ({1}). En gris avec Installer si {1} est introuvable sur cet ordinateur : clic pour voir comment l'installer.",
        ReviewsName = "Revues demandées",
        ReviewsDescription = "Pull requests qui attendent votre revue, sur tout GitHub. Vibre à chaque nouvelle demande. Clic : les ouvre.",
        InboxName = "Boîte GitHub",
        InboxDescription = "Dernier événement GitHub qui vous concerne : revue demandée, mention, assignation, votre PR approuvée, fusionnée, avec des changements demandés ou une CI rouge. Vibre et notifie. Clic : l'ouvre.",
        FocusName = "Minuteur focus",
        FocusDescription = "Minuteur de concentration suivi d'une pause. Clic pour lancer ou arrêter. Vibre à la fin de la session et de la pause.",

        ShortIssues = "Issues",
        ShortPullRequests = "PR",
        ShortCi = "CI",
        ShortAudit = "Failles",
        ShortCommit = "Commit",
        ShortPush = "Push",
        ShortPull = "Pull",
        ShortReviews = "Revues",
        ShortInbox = "GitHub",
        ShortFocus = "Focus",
        ShortBreak = "Pause",

        Ok = "OK",
        Failed = "Échec",
        Running = "En cours",
        Install = "Installer",
        NotInstalled = "non installé",
        LabelSeparator = " : ",
        Minutes = "min",

        EventActionSucceeded = "Action git réussie",
        EventActionFailed = "Action git en échec",
        EventCiPassed = "CI au vert",
        EventCiFailed = "CI en échec",
        EventReviewRequested = "Revue demandée",
        EventFocusEnded = "Fin de la session de focus",
        EventBreakEnded = "Fin de la pause",
        EventMentioned = "Mention sur GitHub",
        EventAssigned = "Assignation sur GitHub",
        EventPrApproved = "PR approuvée",
        EventPrMerged = "PR fusionnée",
        EventChangesRequested = "Changements demandés",
        EventPrCiFailed = "CI en échec sur votre PR",

        InboxReviewRequested = "Revue demandée",
        InboxMention = "Nouvelle mention",
        InboxTeamMention = "Mention de votre équipe",
        InboxAssigned = "Nouvelle assignation",
        InboxApproved = "Approuvée par {0}",
        InboxChangesRequested = "Changements demandés par {0}",
        InboxMerged = "PR fusionnée",
        InboxCiFailed = "CI en échec sur votre PR",
        InboxMore = "{0} autres événements GitHub",

        NotificationTest = "Les notifications fonctionnent. Un clic ouvre vos notifications GitHub.",
        NoProject = "Pas encore de projet courant. Ouvrez un terminal dans un dépôt, ou une fenêtre du Finder sur macOS, puis cliquez à nouveau.",
    };

    public string Code { get; private init; } = "en";
    public string GroupProject { get; private init; } = "";
    public string GroupGitHub { get; private init; } = "";
    public string GroupFocus { get; private init; } = "";
    public string ProjectName { get; private init; } = "";
    public string ProjectDescription { get; private init; } = "";
    public string IssuesName { get; private init; } = "";
    public string IssuesDescription { get; private init; } = "";
    public string PullRequestsName { get; private init; } = "";
    public string PullRequestsDescription { get; private init; } = "";
    public string CiName { get; private init; } = "";
    public string CiDescription { get; private init; } = "";
    public string AuditName { get; private init; } = "";
    public string AuditDescription { get; private init; } = "";
    public string CommitName { get; private init; } = "";
    public string CommitDescription { get; private init; } = "";
    public string PushName { get; private init; } = "";
    public string PushDescription { get; private init; } = "";
    public string PullName { get; private init; } = "";
    public string PullDescription { get; private init; } = "";
    public string AgentName { get; private init; } = "";
    public string AgentDescription { get; private init; } = "";
    public string CodingAgentDescription { get; private init; } = "";
    public string ReviewsName { get; private init; } = "";
    public string ReviewsDescription { get; private init; } = "";
    public string InboxName { get; private init; } = "";
    public string InboxDescription { get; private init; } = "";
    public string FocusName { get; private init; } = "";
    public string FocusDescription { get; private init; } = "";
    public string ShortIssues { get; private init; } = "";
    public string ShortPullRequests { get; private init; } = "";
    public string ShortCi { get; private init; } = "";
    public string ShortAudit { get; private init; } = "";
    public string ShortCommit { get; private init; } = "";
    public string ShortPush { get; private init; } = "";
    public string ShortPull { get; private init; } = "";
    public string ShortReviews { get; private init; } = "";
    public string ShortInbox { get; private init; } = "";
    public string ShortFocus { get; private init; } = "";
    public string ShortBreak { get; private init; } = "";
    public string Ok { get; private init; } = "";
    public string Failed { get; private init; } = "";
    public string Running { get; private init; } = "";
    public string Install { get; private init; } = "";
    public string NotInstalled { get; private init; } = "";
    public string LabelSeparator { get; private init; } = ": ";
    public string Minutes { get; private init; } = "";
    public string EventActionSucceeded { get; private init; } = "";
    public string EventActionFailed { get; private init; } = "";
    public string EventCiPassed { get; private init; } = "";
    public string EventCiFailed { get; private init; } = "";
    public string EventReviewRequested { get; private init; } = "";
    public string EventFocusEnded { get; private init; } = "";
    public string EventBreakEnded { get; private init; } = "";
    public string EventMentioned { get; private init; } = "";
    public string EventAssigned { get; private init; } = "";
    public string EventPrApproved { get; private init; } = "";
    public string EventPrMerged { get; private init; } = "";
    public string EventChangesRequested { get; private init; } = "";
    public string EventPrCiFailed { get; private init; } = "";
    public string InboxReviewRequested { get; private init; } = "";
    public string InboxMention { get; private init; } = "";
    public string InboxTeamMention { get; private init; } = "";
    public string InboxAssigned { get; private init; } = "";
    public string InboxApproved { get; private init; } = "";
    public string InboxChangesRequested { get; private init; } = "";
    public string InboxMerged { get; private init; } = "";
    public string InboxCiFailed { get; private init; } = "";
    public string InboxMore { get; private init; } = "";
    public string NotificationTest { get; private init; } = "";
    public string NoProject { get; private init; } = "";

    // "auto" follows the system language detected by the caller.
    public static Strings For(string? configured, string? systemLanguage)
    {
        var code = string.IsNullOrWhiteSpace(configured) || configured == "auto" ? systemLanguage : configured;
        return code != null && code.StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? French : English;
    }

    public string Label(string name, string value) => name + this.LabelSeparator + value;

    public string CodingAgentText(CodingAgent agent) => string.Format(this.CodingAgentDescription, agent.Name, agent.Command);

    // Title of the notification: what happened.
    public string InboxTitle(InboxEvent e)
    {
        var actor = e.Actor ?? "?";
        return e.Kind switch
        {
            InboxKind.ReviewRequested => this.InboxReviewRequested,
            InboxKind.Mention => this.InboxMention,
            InboxKind.TeamMention => this.InboxTeamMention,
            InboxKind.Assigned => this.InboxAssigned,
            InboxKind.Approved => string.Format(this.InboxApproved, actor),
            InboxKind.ChangesRequested => string.Format(this.InboxChangesRequested, actor),
            InboxKind.Merged => this.InboxMerged,
            _ => this.InboxCiFailed,
        };
    }

    // Text of the notification: where. "acme/api#12 Add search"
    public static string InboxText(InboxEvent e) =>
        (e.Number is { } n ? $"{e.Repo}#{n}" : e.Repo) + (e.Title.Length > 0 ? " " + e.Title : "");

    public string InboxMoreText(int count) => string.Format(this.InboxMore, count);
}
