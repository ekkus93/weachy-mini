#include <mujoco/mujoco.h>

#include <stdio.h>
#include <stdlib.h>

int main(int argc, char** argv)
{
    if(argc != 3)
    {
        (void)fprintf(stderr, "usage: %s INPUT.xml OUTPUT.mjb\n", argv[0]);
        return 2;
    }

    char error[1024] = {0};
    mjModel* const model = mj_loadXML(argv[1], NULL, error, (int)sizeof(error));
    if(model == NULL)
    {
        (void)fprintf(stderr, "MuJoCo model compilation failed: %s\n", error);
        return 1;
    }

    mj_saveModel(model, argv[2], NULL, 0);
    mjModel* const verified = mj_loadModel(argv[2], NULL);
    if(verified == NULL)
    {
        (void)fprintf(stderr, "MuJoCo could not reload the generated MJB\n");
        mj_deleteModel(model);
        (void)remove(argv[2]);
        return 1;
    }

    (void)printf(
        "{\"status\":\"ok\",\"mujoco_version\":\"%s\","
        "\"body_count\":%d,\"joint_count\":%d,\"actuator_count\":%d,"
        "\"position_count\":%d,\"velocity_count\":%d}\n",
        mj_versionString(),
        verified->nbody,
        verified->njnt,
        verified->nu,
        verified->nq,
        verified->nv);
    mj_deleteModel(verified);
    mj_deleteModel(model);
    return 0;
}
