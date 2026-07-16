#!/usr/bin/env bash
set -e

VERSION="$1"
GITHUB_REMOTE_URL="https://github.com/joffrey-b/Windows-Backup-Helper.git"

if [ -z "$VERSION" ]; then
  echo "Usage: ./scripts/publish-github.sh <version>"
  echo "Example: ./scripts/publish-github.sh 0.1.0"
  exit 1
fi

if ! echo "$VERSION" | grep -qE '^[0-9]+\.[0-9]+\.[0-9]+$'; then
  echo "Error: version must be in x.y.z format (e.g. 0.1.0)"
  exit 1
fi

TAG="v$VERSION"
BRANCH="release-$VERSION"

if ! git diff --quiet HEAD; then
  echo "Error: you have uncommitted changes. Commit or stash them before publishing."
  exit 1
fi

if ! git remote get-url github >/dev/null 2>&1; then
  echo "Adding github remote..."
  git remote add github "$GITHUB_REMOTE_URL"
fi

# Clean up temp branch if it already exists from a failed previous run
git branch -D "$BRANCH" 2>/dev/null || true

echo "Fetching GitHub..."
git fetch github

if git show-ref --verify --quiet refs/remotes/github/main; then
  echo "Creating release branch from github/main..."
  git checkout -b "$BRANCH" github/main
  echo "Squashing changes..."
  # --allow-unrelated-histories: github/main's own initial commit (e.g. a repo created with just
  # a LICENSE via the GitHub UI) shares no common ancestor with local main -- harmless to pass
  # even once that's no longer true, since every github/main after the first publish descends
  # from this same local main.
  git merge --squash -X theirs --allow-unrelated-histories main
else
  # No prior GitHub history to reconcile with -- this is the first publish, so the release
  # branch is just the current tree as a single commit with no parent history at all.
  echo "No existing github/main -- this is the first publish. Building the initial squashed commit..."
  git checkout --orphan "$BRANCH"
  git add -A
fi

# Set the public version. Directory.Build.props' <Version> is shared by every project in the
# solution and flows into each exe's AssemblyVersion/FileVersion/InformationalVersion -- local
# GitLab builds may be ahead of what's published here.
if grep -q "<Version>" Directory.Build.props; then
  sed -i "s#<Version>.*</Version>#<Version>$VERSION</Version>#" Directory.Build.props
else
  sed -i "s#<Nullable>enable</Nullable>#<Version>$VERSION</Version>\n    <Nullable>enable</Nullable>#" Directory.Build.props
fi
git add Directory.Build.props

git commit -m "Windows Backup Helper v$VERSION"

echo "Pushing to GitHub main..."
git push github "$BRANCH":main

echo "Tagging..."
# Push tag directly by commit SHA to avoid touching local tags, which may already be used for
# GitLab test builds with different commits.
COMMIT=$(git rev-parse HEAD)
git push github "$COMMIT:refs/tags/$TAG"

echo "Cleaning up..."
git checkout main
git branch -D "$BRANCH"

echo "Syncing github/main into local main..."
git fetch github
# --allow-unrelated-histories: same reason as the squash-merge step above -- needed at least for
# this first publish, harmless afterward once local main and github/main share real ancestry.
git merge github/main -X ours --allow-unrelated-histories --no-edit

echo ""
echo "Done. Tag $TAG pushed to GitHub."
