#!/bin/sh
set -eu

# Runs the actual Deployment Stack create/update for the current azd
# environment. Not plain `az deployment group create` - deliberately
# `az stack group create`, so drift detection and deny settings exist at
# all. See day-24/README.md for why this is a hook calling az directly
# rather than relying on azd's own (currently unconfigurable, in this
# version) alpha deployment-stacks support.

ENV_NAME="${AZURE_ENV_NAME:?AZURE_ENV_NAME must be set - run this via 'azd provision -e <dev|prod>', not directly}"
RESOURCE_GROUP="rg-thinkschool-day17"
STACK_NAME="quoteshub-${ENV_NAME}"
PARAMS_FILE="$(cd "$(dirname "$0")/../.." && pwd)/day-23/params/${ENV_NAME}.bicepparam"
TEMPLATE_FILE="$(cd "$(dirname "$0")/../.." && pwd)/day-23/main.bicep"

if [ ! -f "$PARAMS_FILE" ]; then
  echo "No parameter file for environment '${ENV_NAME}' at ${PARAMS_FILE}" >&2
  exit 1
fi

# --action-on-unmanage detachAll: if a resource is ever removed from
# main.bicep (or the stack itself deleted), Azure stops MANAGING it but
# never deletes it. The dev stack adopts three resources that already
# existed and matter to a real user - the safe default for a stack that
# didn't create what it manages is "let go of it," not "delete it."
# Tightening this to deleteResources is a deliberate future decision,
# not this one.
#
# --deny-settings-mode denyDelete: blocks DELETE on every resource this
# stack manages, from anyone/anything acting outside the stack, while
# still allowing writes (e.g. a hand-applied tag) through. denyWriteAndDelete
# would be more locked-down but would also block the drift demonstration
# this task explicitly asks for - a blocked write and a detected drift
# can't both happen if writes themselves are denied. denyDelete is the
# tightest mode that still lets drift be observed.
az stack group create \
  --name "$STACK_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --template-file "$TEMPLATE_FILE" \
  --parameters "$PARAMS_FILE" \
  --action-on-unmanage detachAll \
  --deny-settings-mode denyDelete \
  --deny-settings-apply-to-child-scopes \
  --description "QuoteHub ${ENV_NAME} - managed by day-24" \
  --yes
