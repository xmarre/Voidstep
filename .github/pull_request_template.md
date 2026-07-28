## Scope

- [ ] Runtime implementation complete
- [ ] Pure logic tests updated
- [ ] API audit updated
- [ ] Documentation updated
- [ ] Release packaging verified

## Verification

- [ ] `python scripts/run_logic_mirror_tests.py`
- [ ] `python scripts/verify_source_invariants.py`
- [ ] `python scripts/validate_api_surface.py references/runtime`
- [ ] `./build.ps1 -Configuration Release -ReferenceRoot ./references/runtime`
- [ ] Bannerlord runtime matrix recorded

Keep this pull request in draft until all planned commits are present. Trigger final review with `@coderabbitai review` only after the authoritative build and packaging checks pass.
