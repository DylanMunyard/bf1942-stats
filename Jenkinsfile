pipeline {
  agent {
    kubernetes {
      yamlFile 'deploy/pod.yaml'
    }
  }
  stages {
    stage('Docker build') {
      steps {
        container('dind') {
            sh '''
                docker build -f deploy/Dockerfile . -t container-registry-service.container-registry:5000/bf42-stats:latest
                docker push container-registry-service.container-registry:5000/bf42-stats:latest
            '''
        }
      }
    }
    stage('Deploy') {
      steps {
        container('kubectl') {
            withKubeConfig([namespace: "bf42-stats"]) {
              sh 'kubectl rollout restart deployment/bf42-stats'
            }
          }
        }
    }
  }
}