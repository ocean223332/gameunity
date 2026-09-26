using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TienTuyen.Combat;
using TienTuyen.Content;

namespace TienTuyen.Combat.Tests
{
    public sealed class ContentCatalogTests
    {
        private static WeaponDefinition Rifle(float damage = 12f, float cooldown = 0.35f,
            float range = 10f, int price = 20, string id = ContentIds.Rifle) =>
            new WeaponDefinition(id, "Súng trường", damage, 1, cooldown, 12, 1.5f, range, price);

        private static EnemyDefinition Infantry(int spawnWeight = 8, string id = ContentIds.Infantry) =>
            new EnemyDefinition(id, "Lính áp sát", EnemyRole.Infantry,
                35f, 2.6f, 8f, 1.4f, 1.2f, 0.25f, spawnWeight, 1, 2);

        [Test]
        public void P2HasThreeWeaponsAndThreeRegularPlusEliteEnemyRolesWithStableUniqueIds()
        {
            var catalog = ContentCatalog.P2;
            Assert.That(catalog.Weapons.Count, Is.EqualTo(3));
            Assert.That(catalog.Enemies.Count, Is.EqualTo(4));
            Assert.That(catalog.Weapons.Select(w => w.Id),
                Is.EquivalentTo(new[] { ContentIds.Rifle, ContentIds.Smg, ContentIds.Shotgun }));
            Assert.That(catalog.Enemies.Select(e => e.Id),
                Is.EquivalentTo(new[] { ContentIds.Infantry, ContentIds.Charger, ContentIds.Shooter, ContentIds.Elite }));
            Assert.That(catalog.Enemies.Select(e => e.Role),
                Is.EquivalentTo(new[] { EnemyRole.Infantry, EnemyRole.Charger, EnemyRole.Shooter, EnemyRole.Elite }));
            Assert.That(catalog.Weapons.Select(w => w.Id).Concat(catalog.Enemies.Select(e => e.Id)).Distinct().Count(),
                Is.EqualTo(7));
        }

        [Test]
        public void P2WeaponsKeepSpecNumbersAndHaveUsableStatBounds()
        {
            var catalog = ContentCatalog.P2;
            var rifle = catalog.GetWeapon(ContentIds.Rifle);
            var smg = catalog.GetWeapon(ContentIds.Smg);
            var shotgun = catalog.GetWeapon(ContentIds.Shotgun);
            Assert.That((rifle.DamagePerHit, rifle.ShotCooldownSeconds, rifle.MagazineSize,
                rifle.ReloadSeconds, rifle.Range, rifle.BasePrice),
                Is.EqualTo((12f, 0.35f, 12, 1.5f, 10f, 20)));
            Assert.That((smg.DamagePerHit, smg.ShotCooldownSeconds, smg.MagazineSize,
                smg.ReloadSeconds, smg.Range, smg.BasePrice),
                Is.EqualTo((5f, 0.12f, 24, 1.4f, 6.5f, 18)));
            Assert.That((shotgun.DamagePerHit, shotgun.PelletsPerShot, shotgun.ShotCooldownSeconds,
                shotgun.MagazineSize, shotgun.ReloadSeconds, shotgun.Range, shotgun.BasePrice),
                Is.EqualTo((4f, 6, 0.85f, 5, 2f, 4.5f, 22)));
            Assert.That(catalog.Weapons.All(w => w.DamagePerHit > 0 && w.DamagePerHit <= 36 &&
                w.ShotCooldownSeconds >= 0.1f && w.ShotCooldownSeconds <= 1.6f &&
                w.Range >= 4.5f && w.Range <= 13f && w.BasePrice >= 18 && w.BasePrice <= 30), Is.True);
        }

        [Test]
        public void P2EnemiesHaveReadableNamesAndExpectedRewards()
        {
            foreach (var enemy in ContentCatalog.P2.Enemies)
            {
                Assert.That(enemy.DisplayName, Is.Not.Empty);
                Assert.That(enemy.MaxHealth, Is.InRange(20f, 300f));
                Assert.That(enemy.MoveSpeed, Is.InRange(1f, 4f));
                Assert.That(enemy.AttackDamage, Is.InRange(1f, 25f));
                Assert.That(enemy.SpawnWeight, Is.GreaterThan(0));
                Assert.That(enemy.ExperienceReward, Is.EqualTo(enemy.Role == EnemyRole.Elite ? 10 : 1));
                Assert.That(enemy.SupplyReward, Is.EqualTo(enemy.Role == EnemyRole.Elite ? 20 : 2));
            }
            Assert.That(ContentCatalog.P2.GetEnemy(ContentIds.Shooter).TelegraphSeconds, Is.EqualTo(0.8f));
        }

        [Test]
        public void ChargerHasDistinctRoleStatsTelegraphAndRegularRewards()
        {
            var charger = ContentCatalog.P2.GetEnemy(ContentIds.Charger);
            Assert.That(charger.Role, Is.EqualTo(EnemyRole.Charger));
            Assert.That(charger.DisplayName, Is.EqualTo("Lính xung kích"));
            Assert.That(charger.MaxHealth, Is.LessThan(ContentCatalog.P2.GetEnemy(ContentIds.Infantry).MaxHealth));
            Assert.That(charger.MoveSpeed, Is.GreaterThan(ContentCatalog.P2.GetEnemy(ContentIds.Infantry).MoveSpeed));
            Assert.That(charger.AttackRange, Is.GreaterThan(charger.AttackCooldownSeconds));
            Assert.That(charger.TelegraphSeconds, Is.GreaterThan(0.5f).And.LessThan(1f));
            Assert.That((charger.ExperienceReward, charger.SupplyReward), Is.EqualTo((1, 2)));
        }

        [Test]
        public void ChargerEligibilityStartsAtWaveTwoWhileEliteIsNotRegular()
        {
            Assert.That(CombatGame.IsEnemyRoleEligibleForWave(EnemyRole.Charger, 1), Is.False);
            Assert.That(CombatGame.IsEnemyRoleEligibleForWave(EnemyRole.Charger, 2), Is.True);
            Assert.That(CombatGame.IsEnemyRoleEligibleForWave(EnemyRole.Shooter, 2), Is.False);
            Assert.That(CombatGame.IsEnemyRoleEligibleForWave(EnemyRole.Shooter, 3), Is.True);
            Assert.That(CombatGame.IsEnemyRoleEligibleForWave(EnemyRole.Elite, 6), Is.False);
        }

        [Test]
        public void CatalogRejectsDuplicateIdsEvenAcrossDefinitionTypes()
        {
            Assert.That(() => new ContentCatalog(new[] { Rifle(), Rifle() }, new EnemyDefinition[0]),
                Throws.ArgumentException.With.Message.Contains(ContentIds.Rifle));
            Assert.That(() => new ContentCatalog(new[] { Rifle(id: ContentIds.Infantry) },
                new[] { Infantry() }),
                Throws.ArgumentException.With.Message.Contains(ContentIds.Infantry));
        }

        [Test]
        public void CatalogRejectsInvalidWeaponDamageCooldownRangeAndPrice()
        {
            var invalid = new[]
            {
                Rifle(damage: 0f), Rifle(damage: float.NaN),
                Rifle(cooldown: 0f), Rifle(cooldown: float.PositiveInfinity),
                Rifle(range: -1f), Rifle(price: 0)
            };
            foreach (var weapon in invalid)
                Assert.That(() => new ContentCatalog(new[] { weapon }, new EnemyDefinition[0]),
                    Throws.ArgumentException);
        }

        [Test]
        public void CatalogRejectsNonPositiveSpawnWeightAndBlankId()
        {
            Assert.That(() => new ContentCatalog(new WeaponDefinition[0], new[] { Infantry(spawnWeight: 0) }),
                Throws.ArgumentException.With.Message.Contains("spawn weight"));
            Assert.That(() => new ContentCatalog(new[] { Rifle(id: " ") }, new EnemyDefinition[0]),
                Throws.ArgumentException);
        }

        [Test]
        public void MissingLookupIsExplicitAndTryLookupIsSafe()
        {
            var catalog = ContentCatalog.P2;
            Assert.That(catalog.TryGetWeapon("weapon.unknown", out _), Is.False);
            Assert.That(catalog.TryGetEnemy(null, out _), Is.False);
            Assert.That(() => catalog.GetWeapon("weapon.unknown"),
                Throws.TypeOf<KeyNotFoundException>().With.Message.Contains("weapon.unknown"));
            Assert.That(() => catalog.GetEnemy("enemy.unknown"),
                Throws.TypeOf<KeyNotFoundException>().With.Message.Contains("enemy.unknown"));
        }
    }
}
