set -euxo pipefail

dotnet clean

find . -iname "bin" | xargs rm -r
find . -iname "obj" | xargs rm -r
