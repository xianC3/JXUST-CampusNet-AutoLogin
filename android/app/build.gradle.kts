plugins {
    id("com.android.application")
}

android {
    namespace = "com.chxyi.campusnet"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.chxyi.campusnet.generic"
        minSdk = 26
        targetSdk = 35
        versionCode = 18
        versionName = "2.5.1"
    }

    sourceSets {
        getByName("main") {
            java.srcDirs("../src")
            res.srcDirs("../res")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    buildTypes {
        debug {
            applicationIdSuffix = ".debug"
            versionNameSuffix = "-debug"
        }
        release {
            isMinifyEnabled = false
        }
    }
}
