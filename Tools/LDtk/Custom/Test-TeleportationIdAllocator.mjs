import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import vm from "node:vm";

const directory = dirname(fileURLToPath(import.meta.url));
const rendererPath = join(directory, "renderer.js");
const source = readFileSync(rendererPath, "utf8");
const start = source.indexOf("data_inst_EntityInstance.assignNextTeleportationId = function(ei)");
const end = source.indexOf("data_inst_EntityInstance.fromJson = function", start);
assert.ok(start >= 0 && end > start, "Teleportation ID allocator was not found in renderer.js.");
assert.equal(
    (source.match(/data_inst_EntityInstance\.assignNextTeleportationId = function/g) ?? []).length,
    1,
    "Teleportation ID allocator must be defined exactly once.");
assert.equal(
    (source.match(/data_inst_EntityInstance\.assignNextTeleportationId\(/g) ?? []).length,
    2,
    "Creation and duplication must both allocate IDs.");

const integerType = {};
const context = {
    data_inst_EntityInstance: {},
    haxe_ds_IntMap: class { constructor() { this.h = {}; } },
    haxe_Exception: { thrown: message => new Error(message) },
    ldtk_FieldType: { F_Int: integerType },
    Std: { string: value => String(value) },
};
vm.runInNewContext(source.slice(start, end), context);

const idFieldDefinition = { identifier: "ID", type: integerType, isArray: false, uid: 101 };
const teleportationDefinition = { identifier: "Teleportation", uid: 38, fieldDefs: [idFieldDefinition] };

function existingEntity(id, iid, levelId) {
    return {
        defUid: teleportationDefinition.uid,
        iid,
        _li: { levelId },
        getFieldInstance: () => ({ valueIsNull: () => false, getInt: () => id }),
    };
}

function targetEntity(cache, levelId = 100) {
    let assigned = null;
    return {
        entity: {
            defUid: teleportationDefinition.uid,
            _li: { levelId },
            _project: { entityIidsCache: { h: cache } },
            get_def: () => teleportationDefinition,
            getFieldInstance: (_definition, create) => {
                assert.equal(create, true);
                return { parseValue: (_index, value) => assigned = Number(value) };
            },
        },
        assigned: () => assigned,
    };
}

const target = targetEntity({
    a: existingEntity(0, "a", 100),
    b: existingEntity(2, "b", 100),
    c: existingEntity(9, "c", 200),
});
context.data_inst_EntityInstance.assignNextTeleportationId(target.entity);
assert.equal(target.assigned(), 3);

const duplicate = targetEntity({
    a: existingEntity(4, "a", 100),
    b: existingEntity(4, "b", 100),
});
assert.throws(
    () => context.data_inst_EntityInstance.assignNextTeleportationId(duplicate.entity),
    /Duplicate Teleportation ID 4/);

const duplicateAcrossLevels = targetEntity({
    a: existingEntity(4, "a", 100),
    b: existingEntity(4, "b", 200),
});
context.data_inst_EntityInstance.assignNextTeleportationId(duplicateAcrossLevels.entity);
assert.equal(duplicateAcrossLevels.assigned(), 5);

console.log("Teleportation ID allocator tests passed.");
