#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

search_generated_regex() {
    local pattern="$1"
    shift

    if command -v rg >/dev/null 2>&1; then
        rg -n -g '*.html' -e "$pattern" "$@"
    else
        grep -R -n -E --include='*.html' "$pattern" "$@"
    fi
}

search_generated_fixed() {
    local text="$1"
    shift

    if command -v rg >/dev/null 2>&1; then
        rg -n -F "$text" "$@"
    else
        grep -R -n -F "$text" "$@"
    fi
}

"${SCRIPT_DIR}/build-docs.sh"

DOC_ROOT="${SCRIPT_DIR}/site/.lunet/build/www"
BUNDLE_CSS="${DOC_ROOT}/css/lite.css"

test -f "${DOC_ROOT}/index.html"
test -f "${DOC_ROOT}/articles/index.html"
test -f "${DOC_ROOT}/articles/getting-started/index.html"
test -f "${DOC_ROOT}/articles/getting-started/overview/index.html"
test -f "${DOC_ROOT}/articles/getting-started/installation/index.html"
test -f "${DOC_ROOT}/articles/getting-started/first-document/index.html"
test -f "${DOC_ROOT}/articles/getting-started/running-the-sample/index.html"
test -f "${DOC_ROOT}/articles/markdown/index.html"
test -f "${DOC_ROOT}/articles/markdown/markdown-text-block/index.html"
test -f "${DOC_ROOT}/articles/markdown/rendering-services/index.html"
test -f "${DOC_ROOT}/articles/markdown/editing/index.html"
test -f "${DOC_ROOT}/articles/markdown/source-mapping/index.html"
test -f "${DOC_ROOT}/articles/markdown/theming/index.html"
test -f "${DOC_ROOT}/articles/markdown/task-lists/index.html"
test -f "${DOC_ROOT}/articles/markdown/plugin-ecosystem/index.html"
test -f "${DOC_ROOT}/articles/extensions/index.html"
test -f "${DOC_ROOT}/articles/extensions/plugin-model/index.html"
test -f "${DOC_ROOT}/articles/extensions/parser-plugins/index.html"
test -f "${DOC_ROOT}/articles/extensions/rendering-plugins/index.html"
test -f "${DOC_ROOT}/articles/extensions/editor-plugins/index.html"
test -f "${DOC_ROOT}/articles/extensions/block-templates/index.html"
test -f "${DOC_ROOT}/articles/development/index.html"
test -f "${DOC_ROOT}/articles/development/build-test-and-docs/index.html"
test -f "${DOC_ROOT}/articles/reference/index.html"
test -f "${DOC_ROOT}/articles/reference/repository-structure/index.html"
test -f "${BUNDLE_CSS}"

if search_generated_regex 'href="[^"]*\.md([?#"][^"]*)?"' "${DOC_ROOT}" | grep -vE 'href="https?://' >/dev/null; then
    echo "Generated docs contain raw .md links."
    exit 1
fi

if search_generated_regex 'href="[^"]*/readme([?#"][^"]*)?"' "${DOC_ROOT}" >/dev/null; then
    echo "Generated docs contain /readme routes instead of directory routes."
    exit 1
fi

if find "${DOC_ROOT}/articles" -name '*.md' -print -quit | grep -q .; then
    echo "Generated docs still contain raw .md article outputs."
    find "${DOC_ROOT}/articles" -name '*.md' -print
    exit 1
fi

if ! search_generated_fixed 'MIT license' "${DOC_ROOT}/index.html" >/dev/null; then
    echo "Generated site footer is missing the project MIT license text."
    exit 1
fi

if ! search_generated_fixed 'https://github.com/wieslawsoltes/ProMarkdown' "${DOC_ROOT}/index.html" >/dev/null; then
    echo "Generated home page is missing the repository link."
    exit 1
fi

if ! search_generated_fixed '/ProMarkdown/articles/' "${DOC_ROOT}/index.html" >/dev/null; then
    echo "Generated home page is missing basepath-prefixed article links."
    exit 1
fi

if ! search_generated_fixed '<p class="lead"><strong>ProMarkdown</strong>' "${DOC_ROOT}/index.html" >/dev/null; then
    echo "Generated home page is missing the rendered hero lead paragraph."
    exit 1
fi

if ! search_generated_fixed '.pm-hero' "${BUNDLE_CSS}" >/dev/null; then
    echo "Generated docs bundle is missing the .pm-hero selector."
    exit 1
fi

if ! search_generated_fixed '.pm-link-card' "${BUNDLE_CSS}" >/dev/null; then
    echo "Generated docs bundle is missing the .pm-link-card selector."
    exit 1
fi
