using System;
using System.IO;
using Sep.Git.Tfs.Commands;
using System.Text.RegularExpressions;

namespace Sep.Git.Tfs.Util
{
    /// <summary>
    /// Stores the state of the <see cref="CheckinOptions"/> before parsing
    /// a git commit message for special actions.  Packages up restore 
    /// actions inside of a <see cref="GenericCaretaker"/> so that any
    /// special actions in the commit message can be temporarily 
    /// set in the semi-global <see cref="CheckinOptions"/> without
    /// affecting state later on.
    /// </summary>
    /// <remarks>
    /// This class extracts the pre-checkin commit message parsing that
    /// enables special git-tfs commands: 
    /// https://github.com/git-tfs/git-tfs/wiki/Special-actions-in-commit-messages
    /// </remarks>
    public class CheckinOptionsHelper
    {
        TextWriter writer;

        public CheckinOptionsHelper(TextWriter writer)
        {
            this.writer = writer;
        }

        public GenericCaretaker UpdateCheckinOptionsForThisCommit(CheckinOptions checkinOptions, string commitMessage)
        {
            Action restoreCheckinComment = ApplyCommitMessage(checkinOptions, commitMessage);
            Action undoWorkItemCommands = ProcessWorkItemCommands(checkinOptions, writer);
            Action undoForceCommand = ProcessForceCommand(checkinOptions, writer);

            return new GenericCaretaker(() =>
            {
                restoreCheckinComment();
                undoWorkItemCommands();
                undoForceCommand();
            });
        }

        private Action ApplyCommitMessage(CheckinOptions checkinOptions, string commitMessage)
        {
            // store existing state
            string originalCheckinComment = checkinOptions.CheckinComment;

            // operate
            checkinOptions.CheckinComment = commitMessage;

            // delegate the restoration of state
            return () =>
            {
                checkinOptions.CheckinComment = originalCheckinComment;
            };
        }

        private Action ProcessWorkItemCommands(CheckinOptions checkinOptions, TextWriter writer)
        {
            MatchCollection workitemMatches;
            if ((workitemMatches = GitTfsConstants.TfsWorkItemRegex.Matches(checkinOptions.CheckinComment)).Count > 0)
            {
                foreach (Match match in workitemMatches)
                {
                    switch (match.Groups["action"].Value)
                    {
                        case "associate":
                            writer.WriteLine("Associating with work item {0}", match.Groups["item_id"]);
                            checkinOptions.WorkItemsToAssociate.Add(match.Groups["item_id"].Value);
                            break;
                        case "resolve":
                            writer.WriteLine("Resolving work item {0}", match.Groups["item_id"]);
                            checkinOptions.WorkItemsToResolve.Add(match.Groups["item_id"].Value);
                            break;
                    }
                }
                checkinOptions.CheckinComment = GitTfsConstants.TfsWorkItemRegex.Replace(checkinOptions.CheckinComment, "").Trim(' ', '\r', '\n');
            }

            // delegate the restoration of state
            return () =>
            {
                checkinOptions.WorkItemsToAssociate.Clear();
                checkinOptions.WorkItemsToResolve.Clear();
            };
        }

        private Action ProcessForceCommand(CheckinOptions checkinOptions, TextWriter writer)
        {
            bool originalForceValue = checkinOptions.Force;
            string originalOverrideReason = checkinOptions.OverrideReason;

            MatchCollection workitemMatches;
            if ((workitemMatches = GitTfsConstants.TfsForceRegex.Matches(checkinOptions.CheckinComment)).Count == 1)
            {
                string overrideReason = workitemMatches[0].Groups["reason"].Value;

                if (!string.IsNullOrWhiteSpace(overrideReason))
                {
                    writer.WriteLine("Forcing the checkin: {0}", overrideReason);
                    checkinOptions.Force = true;
                    checkinOptions.OverrideReason = overrideReason;
                }
                checkinOptions.CheckinComment = GitTfsConstants.TfsForceRegex.Replace(checkinOptions.CheckinComment, "").Trim(' ', '\r', '\n');
            }

            return () =>
            {
                checkinOptions.Force = originalForceValue;
                checkinOptions.OverrideReason = originalOverrideReason;
            };
        }
    }
}
