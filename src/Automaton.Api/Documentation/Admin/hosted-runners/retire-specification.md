---
title: Retire a Specification
order: 70
---

# Retire a Specification

Retire a specification only after replacing every plan mapping that still depends on it.

1. Identify Subscription Plans referencing the specification.
2. Create or select a replacement active specification.
3. Update those plans to reference the replacement.
4. Reprovision affected containers gradually.
5. Confirm that no active plan references the old specification.
6. Mark the old specification inactive.

Do not deactivate first. An inactive specification prevents valid new provisioning for plans that still reference it. Existing containers keep their active snapshot, but future reprovisioning will fail until their plan mapping is corrected.
