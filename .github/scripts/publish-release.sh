#!/usr/bin/env bash
set -euo pipefail

package_dir=$1
tag="v$RELEASE_VERSION"
ref="refs/tags/$tag"
repo="repos/$GITHUB_REPOSITORY"
package_ids=(ObFramework ObFramework.Cli ObFramework.Adapter.Unity)
packages=()
for id in "${package_ids[@]}"; do
  package="$package_dir/$id.$RELEASE_VERSION.nupkg"
  if [[ ! -s "$package" ]]; then
    echo "::error::Missing release package: ${package##*/}"
    exit 1
  fi
  packages+=("$package")
done

prerelease=false
release_options=()
if [[ "$RELEASE_VERSION" == *-* ]]; then
  prerelease=true
  release_options+=(--latest=false)
fi

tag_object=$(gh api "$repo/git/matching-refs/tags/$tag" \
  --jq ".[] | select(.ref == \"$ref\") | [.object.type, .object.sha] | @tsv")
if [[ -n "$tag_object" ]]; then
  IFS=$'\t' read -r type sha <<< "$tag_object"
  while [[ "$type" == tag ]]; do
    object=$(gh api "$repo/git/tags/$sha" --jq '[.object.type, .object.sha] | @tsv')
    IFS=$'\t' read -r type sha <<< "$object"
  done
  if [[ "$type" != commit || "$sha" != "$GITHUB_SHA" ]]; then
    echo "::error::Tag $tag already points to a different Git object."
    exit 1
  fi
fi

# The list endpoint includes drafts when the caller has write access.
release=$(gh api --paginate "$repo/releases?per_page=100" \
  --jq ".[] | select(.tag_name == \"$tag\")")
if [[ -n "$release" ]]; then
  jq -se 'length == 1' <<< "$release" > /dev/null
  if [[ -z "$tag_object" ]] && ! jq -e --arg sha "$GITHUB_SHA" \
    '.draft and .target_commitish == $sha' <<< "$release" > /dev/null; then
    echo "::error::Existing release $tag cannot be linked to $GITHUB_SHA."
    exit 1
  fi
  if [[ "$(jq -r .draft <<< "$release")" == false ]]; then
    for package in "${packages[@]}"; do
      if ! jq -e --arg name "${package##*/}" --argjson prerelease "$prerelease" \
        '.prerelease == $prerelease and any(.assets[]; .name == $name and .state == "uploaded" and .size > 0)' \
        <<< "$release" > /dev/null; then
        echo "::error::Published release $tag is missing an expected package or has the wrong release type."
        exit 1
      fi
    done
    echo "Release $tag is already published for $GITHUB_SHA."
    exit 0
  fi
fi

if [[ -z "$tag_object" ]]; then
  gh api "$repo/git/refs" --field ref="$ref" --field sha="$GITHUB_SHA" > /dev/null
fi

if [[ -z "$release" ]]; then
  gh release create "$tag" --verify-tag --draft --title "$tag" --generate-notes --prerelease="$prerelease" "${release_options[@]}"
fi
gh release upload "$tag" --clobber "${packages[@]}"
gh release edit "$tag" --draft=false --prerelease="$prerelease" "${release_options[@]}"
