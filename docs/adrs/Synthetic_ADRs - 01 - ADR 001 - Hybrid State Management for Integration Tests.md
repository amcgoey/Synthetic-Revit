## ADR 001 - Hybrid State Management for Integration Tests

Status: Accepted
Tier 3 integration tests must execute write operations against the Revit database without causing state pollution between test runs. We decided to use a hybrid approach based on domain complexity: 1) wrap standard DOM modification tests in a TransactionGroup that is explicitly rolled back in [TearDown], 2) spawn NewProjectDocument instances using firm .rte templates for standards verification, and 3) open detached .rvt models only for highly complex geometric or schema topologies. This strategy maximizes test suite execution speed by avoiding file I/O for 90% of tests, while providing absolute state isolation and guaranteed setups for complex edge cases.
