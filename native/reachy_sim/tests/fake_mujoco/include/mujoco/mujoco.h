#ifndef REACHY_FAKE_MUJOCO_H
#define REACHY_FAKE_MUJOCO_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define mjVERSION_HEADER 3009000

typedef double mjtNum;
typedef int64_t mjtSize;
typedef unsigned char mjtByte;

enum { mjNWARNING = 8 };
enum { mjOBJ_KEY = 15 };
enum {
    mjCNSTR_EQUALITY = 0,
    mjCNSTR_CONTACT_FRICTIONLESS = 5
};
enum {
    mjSTATE_TIME = 1 << 0,
    mjSTATE_QPOS = 1 << 1,
    mjSTATE_QVEL = 1 << 2,
    mjSTATE_ACT = 1 << 3,
    mjSTATE_HISTORY = 1 << 4,
    mjSTATE_WARMSTART = 1 << 5,
    mjSTATE_CTRL = 1 << 6,
    mjSTATE_QFRC_APPLIED = 1 << 7,
    mjSTATE_XFRC_APPLIED = 1 << 8,
    mjSTATE_EQ_ACTIVE = 1 << 9,
    mjSTATE_MOCAP_POS = 1 << 10,
    mjSTATE_MOCAP_QUAT = 1 << 11,
    mjSTATE_USERDATA = 1 << 12,
    mjSTATE_PLUGIN = 1 << 13,
    mjSTATE_PHYSICS = mjSTATE_QPOS | mjSTATE_QVEL | mjSTATE_ACT | mjSTATE_HISTORY,
    mjSTATE_FULLPHYSICS = mjSTATE_TIME | mjSTATE_PHYSICS | mjSTATE_PLUGIN,
    mjSTATE_USER = mjSTATE_CTRL | mjSTATE_QFRC_APPLIED | mjSTATE_XFRC_APPLIED |
                   mjSTATE_EQ_ACTIVE | mjSTATE_MOCAP_POS | mjSTATE_MOCAP_QUAT |
                   mjSTATE_USERDATA,
    mjSTATE_INTEGRATION = mjSTATE_FULLPHYSICS | mjSTATE_USER | mjSTATE_WARMSTART
};

typedef struct mjVFS {
    const void* buffer;
    int buffer_size;
} mjVFS;

typedef struct mjOption {
    mjtNum timestep;
} mjOption;

typedef struct mjModel {
    mjOption opt;
    int nq;
    int nv;
    int na;
    int nu;
    int neq;
    int nbody;
    int njnt;
    int nsite;
    int ncam;
    int nkey;
    int nmocap;
    int nuserdata;
    mjtByte* actuator_ctrllimited;
    mjtNum* actuator_ctrlrange;
} mjModel;

typedef struct mjWarningStat {
    int lastinfo;
    int number;
} mjWarningStat;

typedef struct mjData {
    mjtNum time;
    int ncon;
    mjtSize nefc;
    mjtNum* qpos;
    mjtNum* qvel;
    mjtNum* qacc;
    mjtNum* act;
    mjtNum* ctrl;
    mjtNum* qacc_warmstart;
    mjtNum* qfrc_applied;
    mjtNum* xfrc_applied;
    mjtByte* eq_active;
    mjtNum* mocap_pos;
    mjtNum* mocap_quat;
    mjtNum* userdata;
    mjtNum* efc_pos;
    int* efc_type;
    mjtNum* xpos;
    mjtNum* xquat;
    mjWarningStat warning[mjNWARNING];
    int fake_steps;
    int fake_emit_warning;
} mjData;

void mj_defaultVFS(mjVFS* vfs);
int mj_addBufferVFS(mjVFS* vfs, const char* name, const void* buffer, int nbuffer);
void mj_deleteVFS(mjVFS* vfs);
mjModel* mj_loadXML(const char* filename, const mjVFS* vfs, char* error, int error_size);
mjModel* mj_loadModelBuffer(const void* buffer, int buffer_size);
void mj_saveModel(
    const mjModel* model,
    const char* filename,
    void* buffer,
    int buffer_size);
mjModel* mj_loadModel(const char* filename, const mjVFS* vfs);
mjData* mj_makeData(const mjModel* model);
void mj_resetData(const mjModel* model, mjData* data);
void mj_resetDataKeyframe(const mjModel* model, mjData* data, int key);
void mj_forward(const mjModel* model, mjData* data);
void mj_step(const mjModel* model, mjData* data);
void mj_deleteData(mjData* data);
void mj_deleteModel(mjModel* model);
int mj_name2id(const mjModel* model, int type, const char* name);
int mj_stateSize(const mjModel* model, int sig);
void mj_getState(const mjModel* model, const mjData* data, mjtNum* state, int sig);
void mj_setState(const mjModel* model, mjData* data, const mjtNum* state, int sig);
void mj_applyFT(const mjModel* model, mjData* data, const mjtNum force[3], const mjtNum torque[3], const mjtNum point[3], int body, mjtNum* qfrc_target);
void mju_zero(mjtNum* values, int count);
int mj_version(void);
const char* mj_versionString(void);

#ifdef __cplusplus
}
#endif

#endif
