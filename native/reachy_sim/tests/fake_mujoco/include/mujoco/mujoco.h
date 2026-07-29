#ifndef REACHY_FAKE_MUJOCO_H
#define REACHY_FAKE_MUJOCO_H

#include <stddef.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef double mjtNum;

enum { mjNWARNING = 8 };

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
} mjModel;

typedef struct mjWarningStat {
    int lastinfo;
    int number;
} mjWarningStat;

typedef struct mjData {
    mjtNum time;
    int nefc;
    mjtNum* qpos;
    mjtNum* qvel;
    mjtNum* qacc;
    mjtNum* act;
    mjtNum* ctrl;
    mjtNum* efc_pos;
    mjWarningStat warning[mjNWARNING];
} mjData;

void mj_defaultVFS(mjVFS* vfs);
int mj_addBufferVFS(mjVFS* vfs, const char* name, const void* buffer, int nbuffer);
void mj_deleteVFS(mjVFS* vfs);
mjModel* mj_loadXML(const char* filename, const mjVFS* vfs, char* error, int error_size);
mjData* mj_makeData(const mjModel* model);
void mj_step(const mjModel* model, mjData* data);
void mj_deleteData(mjData* data);
void mj_deleteModel(mjModel* model);

#ifdef __cplusplus
}
#endif

#endif
