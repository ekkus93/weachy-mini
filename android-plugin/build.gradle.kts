import org.gradle.api.tasks.compile.JavaCompile

plugins {
    id("com.android.library") version "9.3.1"
}

android {
    namespace = "com.ekkus.weachy.bridge"
    compileSdk = 37
    ndkVersion = "28.2.13676358"

    defaultConfig {
        minSdk = 31
        consumerProguardFiles("consumer-rules.pro")
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    lint {
        abortOnError = true
        checkDependencies = true
        checkReleaseBuilds = true
        warningsAsErrors = true
    }

    buildFeatures {
        buildConfig = false
    }

    sourceSets {
        getByName("main").java.srcDir(
            "../Assets/Plugins/Android/ReachyOnDeviceAsr.androidlib/src/main/java")
    }
}

dependencies {
    compileOnly("androidx.annotation:annotation:1.10.0")
}

tasks.withType<JavaCompile>().configureEach {
    options.compilerArgs.addAll(listOf("-Xlint:all", "-Werror"))
}
