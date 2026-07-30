#ifndef REACHY_TEST_FAKE_MUJOCO_H
#define REACHY_TEST_FAKE_MUJOCO_H

#define REACHY_FAKE_MUJOCO 1
#define mjVERSION_HEADER 3009000
#define mjSTATE_INTEGRATION 0x01
#define mjOBJ_KEY 1
#define mjCNSTR_EQUALITY 0
#define mjCNSTR_CONTACT_FRICTIONLESS 1
#define mjNWARNING 8

typedef double mjtNum;
typedef unsigned char mjtByte;
typedef long long mjtSize;

typedef struct mjWarningStat {
    int lastinfo;
    int number;
} mjWarningStat;

typedef struct mjOption {
    mjtNum timestep;
} mjOption;

typedef struct mjModel {
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
    mjOption opt;
    mjtByte* actuator_ctrllimited;
    mjtNum* actuator_ctrlrange;
} mjModel;

typedef struct mjData {
    mjtNum time;
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
    mjWarningStat warning[mjNWARNING];
    mjtSize nefc;
    mjtNum* efc_pos;
    int* efc_type;
    int ncon;
    mjtNum* xpos;
    mjtNum* xquat;
    int fake_steps;
    int fake_emit_warning;
} mjData;

typedef struct mjVFS {
    const void* buffer;
    int buffer_size;
} mjVFS;

void mj_defaultVFS(mjVFS* vfs);
int mj_addBufferVFS(mjVFS* vfs, const char* name, const void* buffer, int nbuffer);
void mj_deleteVFS(mjVFS* vfs);
mjModel* mj_loadXML(
    const char* filename,
    const mjVFS* vfs,
    char* error,
    int error_size);
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
void mj_applyFT(
    const mjModel* model,
    mjData* data,
    const mjtNum force[3],
    const mjtNum torque[3],
    const mjtNum point[3],
    int body,
    mjtNum* qfrc_target);
void mju_zero(mjtNum* values, int count);
int mj_version(void);
const char* mj_versionString(void);

#endif
