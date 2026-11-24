using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using NUnit.Framework;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;

namespace SyntheticTests.RevitDOM
{
    [TestFixture]
    [UnitTest]
    public class PocoIdentityServicePropertyTests
    {
        private static readonly string[] DistinctClassPool =
        {
            "Synthetic.WallType",
            "Synthetic.Material",
            "Synthetic.DoorType",
            "Synthetic.FloorType",
            "Synthetic.ViewPlan"
        };

        private static readonly Gen<string> GenNonEmptyClass = Gen.OneOfConst(DistinctClassPool);

        private static readonly Gen<string> GenClassIncludingWildcardAndCase = Gen.OneOfConst(
            string.Empty,
            "Synthetic.WallType",
            "SYNTHETIC.WALLTYPE",
            "Synthetic.Material",
            "synthetic.material",
            "Synthetic.DoorType");

        private static readonly Gen<string> GenCategoryPool = Gen.OneOfConst(
            string.Empty,
            "Walls",
            "walls",
            "Materials",
            "Doors");

        private static readonly Gen<string?> GenUniqueIdPool = Gen.OneOfConst<string?>(
            null,
            string.Empty,
            "uid-1",
            "UID-1",
            "uid-2",
            "uid-3");

        private static readonly Gen<long> GenElementIdPool = Gen.OneOfConst<long>(
            -2000100,
            -1,
            0,
            101,
            202,
            303);

        private static readonly Gen<string> GenNamePool = Gen.OneOfConst(
            string.Empty,
            "Alpha",
            "alpha",
            "Beta",
            "Gamma",
            "Delta");

        private static readonly Gen<string> GenAliasToken = Gen.OneOfConst(
            "Alpha",
            "ALPHA",
            "Beta",
            "Gamma",
            "Legacy_1",
            "legacy_1");

        private static readonly Gen<List<string>?> GenAliasesPool = Gen.OneOf(
            Gen.Const((List<string>?)null),
            GenAliasToken.List[0, 3].Select(list => (List<string>?)list));

        private static readonly Gen<ElementIdModel> GenIdentityModel = Gen.Select(
            GenUniqueIdPool,
            GenElementIdPool,
            GenClassIncludingWildcardAndCase,
            GenCategoryPool,
            GenNamePool,
            GenAliasesPool,
            (uniqueId, id, cls, category, name, aliases) => new ElementIdModel
            {
                UniqueId = uniqueId,
                Id = id,
                Class = cls,
                Category = category,
                Name = name,
                Aliases = aliases
            });

        private static readonly Gen<ElementIdModel?> GenNullableIdentityModel =
            Gen.OneOf(Gen.Const((ElementIdModel?)null), GenIdentityModel.Select(m => (ElementIdModel?)m));

        private static readonly Gen<(string ClassA, string ClassB)> GenDistinctNonEmptyClasses =
            Gen.Select(GenNonEmptyClass, GenNonEmptyClass)
                .Where((a, b) => !string.Equals(a, b, StringComparison.OrdinalIgnoreCase));

        private static readonly Gen<string> GenTokenString =
            Gen.String[Gen.Char["abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-"], 3, 12];

        [Test]
        public void AreSameIdentity_ArbitraryModels_IsDeterministicReflexiveAndSymmetric()
        {
            var service = new PocoIdentityService();

            Gen.Select(GenNullableIdentityModel, GenNullableIdentityModel)
                .Sample((a, b) =>
                {
                    // Determinism
                    bool ab1 = service.AreSameIdentity(a, b);
                    bool ab2 = service.AreSameIdentity(a, b);
                    Assert.AreEqual(ab1, ab2, "AreSameIdentity(ElementIdModel, ElementIdModel) must be deterministic.");

                    // Symmetry on ElementIdModel?
                    bool ba = service.AreSameIdentity(b, a);
                    Assert.AreEqual(ab1, ba, "AreSameIdentity(ElementIdModel, ElementIdModel) must be symmetric.");

                    // Reference reflexivity
                    Assert.IsTrue(service.AreSameIdentity(a, a), "AreSameIdentity(a, a) must be reflexive.");

                    // Clone reflexivity
                    if (a != null)
                    {
                        ElementIdModel clone = CloneIdentity(a);
                        Assert.IsTrue(service.AreSameIdentity(a, clone), "Value-identical clone must satisfy reflexivity.");
                        Assert.IsTrue(service.AreSameIdentity(clone, a));
                    }

                    // ElementModel overloads: determinism, symmetry, and reflexivity
                    ElementModel? elemA = a != null ? new ElementModel { ElementId = CloneIdentity(a) } : null;
                    ElementModel? elemB = b != null ? new ElementModel { ElementId = CloneIdentity(b) } : null;

                    Assert.AreEqual(ab1, service.AreSameIdentity(elemA, elemB));
                    Assert.AreEqual(ab1, service.AreSameIdentity(elemB, elemA));
                    Assert.AreEqual(ab1, service.AreSameIdentity(a, elemB));
                    Assert.AreEqual(ab1, service.AreSameIdentity(elemB, a));
                    Assert.IsTrue(service.AreSameIdentity(elemA, elemA));

                    if (elemA != null)
                    {
                        var elemClone = new ElementModel { ElementId = CloneIdentity(elemA.ElementId) };
                        Assert.IsTrue(service.AreSameIdentity(elemA, elemClone));
                        Assert.IsTrue(service.AreSameIdentity(a, elemClone));
                        Assert.IsTrue(service.AreSameIdentity(elemClone, a));
                    }
                }, iter: 500);
        }

        [Test]
        public void IdentityComparison_DistinctNonEmptyClasses_StrictlyRejectsCrossTypeEvenWhenAllOtherFieldsMatch()
        {
            var service = new PocoIdentityService();

            Gen.Select(
                    GenDistinctNonEmptyClasses,
                    GenTokenString,
                    Gen.OneOf(Gen.Int[1, 100000], Gen.Int[-2000200, -2]).Select(i => (long)i),
                    GenTokenString,
                    GenTokenString,
                    Gen.Bool)
                .Sample((classes, uniqueId, id, category, name, testWildcardEmptyClass) =>
                {
                    var aliases = new List<string> { name, name + "_Alias" };

                    var idA = new ElementIdModel
                    {
                        UniqueId = uniqueId,
                        Id = id,
                        Class = classes.ClassA,
                        Category = category,
                        Name = name,
                        Aliases = new List<string>(aliases)
                    };

                    var idB = new ElementIdModel
                    {
                        UniqueId = uniqueId,
                        Id = id,
                        Class = classes.ClassB,
                        Category = category,
                        Name = name,
                        Aliases = new List<string>(aliases)
                    };

                    var elemA = new ElementModel { ElementId = CloneIdentity(idA) };
                    var elemB = new ElementModel { ElementId = CloneIdentity(idB) };

                    Assert.IsFalse(service.AreSameIdentity(idA, idB));
                    Assert.IsFalse(service.AreSameIdentity(idB, idA));
                    Assert.IsFalse(service.AreSameIdentity(elemA, elemB));
                    Assert.IsFalse(service.AreSameIdentity(idA, elemB));
                    Assert.IsFalse(service.AreSameIdentity(elemA, idB));

                    Assert.IsNull(
                        service.ResolveElement(idA, new[] { elemB }),
                        "ResolveElement must reject cross-type match when both Classes are non-empty and distinct.");

                    if (testWildcardEmptyClass)
                    {
                        // Wildcard behavior: empty Class == "" matches on UniqueId or Id.
                        var wildcardRef = new ElementIdModel { UniqueId = uniqueId, Id = id, Class = string.Empty };
                        Assert.IsTrue(service.AreSameIdentity(wildcardRef, idB));
                        Assert.IsTrue(service.AreSameIdentity(idB, wildcardRef));
                        Assert.AreSame(elemB, service.ResolveElement(wildcardRef, new[] { elemB }));
                    }
                }, iter: 400);
        }

        private static readonly Gen<int[]> GenPermutation4 = Gen.Select(
            Gen.Int.Uniform,
            Gen.Int.Uniform,
            Gen.Int.Uniform,
            Gen.Int.Uniform,
            (k0, k1, k2, k3) =>
            {
                var keyed = new[]
                {
                    (Key: k0, Index: 0),
                    (Key: k1, Index: 1),
                    (Key: k2, Index: 2),
                    (Key: k3, Index: 3)
                };
                Array.Sort(keyed, (left, right) =>
                {
                    int cmp = left.Key.CompareTo(right.Key);
                    return cmp != 0 ? cmp : left.Index.CompareTo(right.Index);
                });
                return new[] { keyed[0].Index, keyed[1].Index, keyed[2].Index, keyed[3].Index };
            });

        [Test]
        public void ResolveElement_FiveStepFallbackPrecedence_EnforcesUniqueIdOverIdOverClassNameOverAliases()
        {
            var service = new PocoIdentityService();

            Gen.Select(
                    GenNonEmptyClass,
                    Gen.OneOf(
                        Gen.Int[1, 50000].Select(i => (long)i),
                        Gen.Int[-2000500, -2000100].Select(i => (long)i)),
                    GenTokenString.Select(s => "u_" + s),
                    GenTokenString.Select(s => "n_" + s),
                    GenTokenString.Select(s => "a_" + s),
                    GenPermutation4)
                .Sample((targetClass, targetId, baseUnique, baseName, aliasSeed, order) =>
                {
                    string targetUniqueId = baseUnique + "_Target";
                    string targetName = baseName + "_Target";
                    string aliasToken = aliasSeed + "_SharedAlias";

                    var candUnique = new ElementModel
                    {
                        UniqueId = targetUniqueId,
                        Id = targetId + 100000,
                        Class = targetClass,
                        Name = baseName + "_UniqueOnly"
                    };

                    var candId = new ElementModel
                    {
                        UniqueId = baseUnique + "_OtherId",
                        Id = targetId,
                        Class = targetClass,
                        Name = baseName + "_IdOnly"
                    };

                    var candName = new ElementModel
                    {
                        UniqueId = baseUnique + "_OtherName",
                        Id = targetId + 200000,
                        Class = targetClass,
                        Name = targetName
                    };

                    var candAlias = new ElementModel
                    {
                        UniqueId = baseUnique + "_OtherAlias",
                        Id = targetId + 300000,
                        Class = targetClass,
                        Name = baseName + "_AliasOnly",
                        Aliases = new List<string> { aliasToken }
                    };

                    ElementModel[] orderedBase = { candUnique, candId, candName, candAlias };
                    var pool = order.Select(idx => orderedBase[idx]).ToList();

                    // Step 1: UniqueId takes precedence over Id, Name, and Aliases regardless of pool order.
                    var queryAll = new ElementIdModel
                    {
                        UniqueId = targetUniqueId.ToUpperInvariant(),
                        Id = targetId,
                        Class = targetClass,
                        Name = targetName,
                        Aliases = new List<string> { aliasToken }
                    };
                    ElementModel? resolvedStep1 = service.ResolveElement(queryAll, pool);
                    Assert.AreSame(candUnique, resolvedStep1, "Step 1 (UniqueId) must take precedence over Id, Name, and Aliases.");
                    Assert.AreSame(resolvedStep1, service.ResolveElement(queryAll, pool), "ResolveElement must be deterministic.");

                    // Step 2: Id takes precedence over Name and Aliases when UniqueId is absent.
                    var queryId = new ElementIdModel
                    {
                        UniqueId = null,
                        Id = targetId,
                        Class = targetClass,
                        Name = targetName,
                        Aliases = new List<string> { aliasToken }
                    };
                    Assert.AreSame(candId, service.ResolveElement(queryId, pool), "Step 2 (Id) must take precedence over Name and Aliases.");

                    // Step 4: Class + Name takes precedence over Aliases when UniqueId and Id are absent.
                    var queryName = new ElementIdModel
                    {
                        UniqueId = null,
                        Id = 0,
                        Class = targetClass.ToLowerInvariant(),
                        Name = targetName.ToUpperInvariant(),
                        Aliases = new List<string> { aliasToken }
                    };
                    Assert.AreSame(candName, service.ResolveElement(queryName, pool), "Step 4 (Class+Name) must take precedence over Aliases.");

                    // Step 5: Aliases fallback resolves across model.Aliases<->p.Aliases, model.Aliases<->p.Name, and model.Name<->p.Aliases.
                    var queryAlias = new ElementIdModel
                    {
                        UniqueId = null,
                        Id = 0,
                        Class = targetClass,
                        Name = baseName + "_NoDirectNameMatch",
                        Aliases = new List<string> { aliasToken.ToUpperInvariant() }
                    };
                    Assert.AreSame(candAlias, service.ResolveElement(queryAlias, pool), "Step 5b (Aliases cross-match) must resolve when higher tiers miss.");

                    var queryAliasToName = new ElementIdModel
                    {
                        UniqueId = null,
                        Id = 0,
                        Class = targetClass,
                        Name = baseName + "_NoDirectNameMatch",
                        Aliases = new List<string> { candAlias.Name.ToUpperInvariant() }
                    };
                    Assert.AreSame(candAlias, service.ResolveElement(queryAliasToName, pool), "Step 5a (model.Aliases matching candidate.Name) must resolve.");

                    var queryNameToAlias = new ElementIdModel
                    {
                        UniqueId = null,
                        Id = 0,
                        Class = targetClass,
                        Name = aliasToken.ToLowerInvariant()
                    };
                    Assert.AreSame(candAlias, service.ResolveElement(queryNameToAlias, pool), "Step 5c (model.Name matching candidate.Aliases) must resolve.");

                    // Step 3: Strict cross-type rejection across all tiers when a different non-empty Class is requested.
                    string wrongClass = DistinctClassPool.First(c => !string.Equals(c, targetClass, StringComparison.Ordinal));
                    var wrongClassQuery = new ElementIdModel
                    {
                        UniqueId = targetUniqueId,
                        Id = targetId,
                        Class = wrongClass,
                        Name = targetName,
                        Aliases = new List<string> { aliasToken }
                    };
                    Assert.IsNull(service.ResolveElement(wrongClassQuery, pool));

                    // ResolveElements preserves deterministic resolved subset.
                    List<ElementModel> batch = service.ResolveElements(new[] { queryAll, queryId, wrongClassQuery }, pool).ToList();
                    CollectionAssert.AreEqual(new[] { candUnique, candId }, batch);
                }, iter: 300);
        }

        [Test]
        public void AreSameIdentity_FiveStepPrecedenceAndCategoryGuard_MatchesSpec()
        {
            var service = new PocoIdentityService();

            Gen.Select(
                    GenNonEmptyClass,
                    Gen.Int[1, 50000],
                    GenTokenString.Select(s => "u_" + s),
                    GenTokenString.Select(s => "n_" + s),
                    GenTokenString.Select(s => "c_" + s),
                    GenTokenString.Select(s => "a_" + s))
                .Sample((cls, idVal, u1, name1, cat1, alias1) =>
                {
                    string u2 = u1 + "_Diff";
                    string name2 = name1 + "_Diff";
                    string cat2 = cat1 + "_Diff";

                    // Step 1: Matching UniqueId ignores differing Id, Category, Name, Aliases.
                    var aUnique = new ElementIdModel { UniqueId = u1, Id = idVal, Class = cls, Category = cat1, Name = name1 };
                    var bUnique = new ElementIdModel { UniqueId = u1.ToUpperInvariant(), Id = idVal + 1, Class = cls, Category = cat2, Name = name2 };
                    Assert.IsTrue(service.AreSameIdentity(aUnique, bUnique));

                    // Step 1 rejection: Differing non-empty UniqueIds reject even when Id, Class, Category, Name match.
                    var bDiffUnique = new ElementIdModel { UniqueId = u2, Id = idVal, Class = cls, Category = cat1, Name = name1 };
                    Assert.IsFalse(service.AreSameIdentity(aUnique, bDiffUnique));

                    // Step 2: Without UniqueId, matching non-zero Id ignores differing Category and Name.
                    var aId = new ElementIdModel { Id = idVal, Class = cls, Category = cat1, Name = name1 };
                    var bId = new ElementIdModel { Id = idVal, Class = cls, Category = cat2, Name = name2 };
                    Assert.IsTrue(service.AreSameIdentity(aId, bId));

                    // Step 4: Without UniqueId and with differing non-zero Ids, Class + Category + Name matches.
                    var aName = new ElementIdModel { Id = idVal, Class = cls, Category = cat1, Name = name1, Aliases = new List<string> { alias1 } };
                    var bNameSameCat = new ElementIdModel { Id = idVal + 1, Class = cls.ToUpperInvariant(), Category = cat1.ToUpperInvariant(), Name = name1.ToUpperInvariant() };
                    var bNameDiffCat = new ElementIdModel { Id = idVal + 1, Class = cls, Category = cat2, Name = name1, Aliases = new List<string> { alias1 } };
                    var bNameWildcardCat = new ElementIdModel { Id = idVal + 1, Class = cls, Category = string.Empty, Name = name1 };

                    Assert.IsTrue(service.AreSameIdentity(aName, bNameSameCat));
                    Assert.IsTrue(service.AreSameIdentity(aName, bNameWildcardCat));
                    Assert.IsFalse(service.AreSameIdentity(aName, bNameDiffCat), "Distinct non-empty Categories must prevent Step 4 Name match.");

                    // Step 5: Without UniqueId/Id/Name match, overlapping Aliases or Name<->Alias cross-match succeeds within same Class.
                    var aAlias = new ElementIdModel { Id = idVal, Class = cls, Category = cat1, Name = name1, Aliases = new List<string> { alias1 } };
                    var bAliasByName = new ElementIdModel { Id = idVal + 1, Class = cls, Category = cat2, Name = alias1.ToUpperInvariant() };
                    var bAliasBySharedAlias = new ElementIdModel { Id = idVal + 1, Class = cls, Category = cat2, Name = name2, Aliases = new List<string> { alias1.ToLowerInvariant() } };

                    Assert.IsTrue(service.AreSameIdentity(aAlias, bAliasByName));
                    Assert.IsTrue(service.AreSameIdentity(aAlias, bAliasBySharedAlias));
                }, iter: 300);
        }

        private static ElementIdModel CloneIdentity(ElementIdModel source)
        {
            return new ElementIdModel
            {
                UniqueId = source.UniqueId,
                Id = source.Id,
                Class = source.Class,
                Category = source.Category,
                Name = source.Name,
                Aliases = source.Aliases != null ? new List<string>(source.Aliases) : null,
                IsTemplate = source.IsTemplate
            };
        }
    }
}
